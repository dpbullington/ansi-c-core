namespace AnsiCCore.Semantics.Types;

/// <summary>A pointer to <see cref="Pointee"/>.</summary>
public sealed class PointerType : CType
{
    public PointerType(QualType pointee) => Pointee = pointee;
    public QualType Pointee { get; }
    public override string Display => $"{Pointee} *";
}

/// <summary>An array of <see cref="Element"/>; <see cref="Length"/> is null when the bound is unknown (<c>[]</c>).</summary>
public sealed class ArrayType : CType
{
    public ArrayType(QualType element, long? length)
    {
        Element = element;
        Length = length;
    }

    public QualType Element { get; }
    public long? Length { get; }

    /// <summary>An array of unknown bound is an incomplete type.</summary>
    public override bool IsComplete => Length is not null && Element.Type.IsComplete;
    public override string Display => Length is null ? $"{Element}[]" : $"{Element}[{Length}]";
}

/// <summary>One parameter of a function prototype.</summary>
public sealed record Parameter(QualType Type, string? Name);

/// <summary>
/// A function type. <see cref="HasPrototype"/> distinguishes a declared parameter list
/// (including <c>(void)</c>, which is an empty prototype) from an old-style <c>()</c> with no
/// information.
/// </summary>
public sealed class FunctionType : CType
{
    public FunctionType(QualType returnType, IReadOnlyList<Parameter> parameters, bool isVariadic, bool hasPrototype)
    {
        ReturnType = returnType;
        Parameters = parameters;
        IsVariadic = isVariadic;
        HasPrototype = hasPrototype;
    }

    public QualType ReturnType { get; }
    public IReadOnlyList<Parameter> Parameters { get; }
    public bool IsVariadic { get; }
    public bool HasPrototype { get; }

    /// <summary>Function types are not object types and have no size.</summary>
    public override bool IsComplete => false;

    public override string Display
    {
        get
        {
            string ps = !HasPrototype ? string.Empty
                : Parameters.Count == 0 ? "void"
                : string.Join(", ", Parameters.Select(p => p.Type.ToString()));
            if (IsVariadic) ps += ps.Length == 0 ? "..." : ", ...";
            return $"{ReturnType} ({ps})";
        }
    }
}

/// <summary>A member of a struct or union. <see cref="Offset"/> is the computed byte offset.</summary>
public sealed class Field
{
    public Field(string? name, QualType type, int? bitWidth)
    {
        Name = name;
        Type = type;
        BitWidth = bitWidth;
    }

    public string? Name { get; }
    public QualType Type { get; }
    public int? BitWidth { get; }

    /// <summary>Byte offset of the field (for a bit-field, of its storage unit).</summary>
    public long Offset { get; internal set; }

    /// <summary>For a bit-field, the bit position within its storage unit; 0 otherwise.</summary>
    public int BitOffset { get; internal set; }

    public bool IsBitField => BitWidth is not null;
}

/// <summary>
/// A <c>struct</c> or <c>union</c> type. Created incomplete (forward declaration) and later
/// completed when its body is resolved — a settable design so self-referential members
/// (<c>struct Node { struct Node *next; };</c>) can reference the type while it is still being built.
/// </summary>
public sealed class RecordType : CType
{
    public RecordType(bool isUnion, string? tag)
    {
        IsUnion = isUnion;
        Tag = tag;
    }

    public bool IsUnion { get; }
    public string? Tag { get; }

    private IReadOnlyList<Field> _fields = [];
    public IReadOnlyList<Field> Fields => _fields;

    public override bool IsComplete => _complete;
    private bool _complete;

    public long Size { get; private set; }
    public int Alignment { get; private set; } = 1;

    public void Complete(IReadOnlyList<Field> fields, long size, int alignment)
    {
        _fields = fields;
        Size = size;
        Alignment = alignment;
        _complete = true;
    }

    public Field? FindField(string name) => _fields.FirstOrDefault(f => f.Name == name);

    public override string Display => $"{(IsUnion ? "union" : "struct")} {Tag ?? "<anonymous>"}";
}

/// <summary>An <c>enum</c> type. Compatible with its underlying integer type (<c>int</c>).</summary>
public sealed class EnumType : CType
{
    public EnumType(string? tag) => Tag = tag;

    public string? Tag { get; }
    public IntegerType UnderlyingType => CTypes.Int;

    private IReadOnlyList<(string Name, long Value)> _constants = [];
    public IReadOnlyList<(string Name, long Value)> Constants => _constants;

    public override bool IsComplete => _complete;
    private bool _complete;

    public void Complete(IReadOnlyList<(string Name, long Value)> constants)
    {
        _constants = constants;
        _complete = true;
    }

    public override string Display => $"enum {Tag ?? "<anonymous>"}";
}
