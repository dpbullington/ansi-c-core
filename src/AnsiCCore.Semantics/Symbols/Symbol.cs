using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Semantics.Symbols;

/// <summary>What an ordinary-namespace identifier denotes.</summary>
public enum SymbolKind
{
    Typedef,
    Variable,
    Function,
    Parameter,
    EnumConstant,
}

/// <summary>C storage-class specifiers (minus <c>typedef</c>, which is modeled as a <see cref="SymbolKind"/>).</summary>
public enum StorageClass
{
    None,
    Extern,
    Static,
    Auto,
    Register,
}

/// <summary>
/// A declared identifier in the ordinary namespace (variable, function, typedef, parameter, or
/// enum constant). Tags (struct/union/enum names) live in a separate namespace — see
/// <see cref="Scope"/>.
/// </summary>
public sealed class Symbol
{
    public Symbol(string name, SymbolKind kind, QualType type, StorageClass storage, SourceLocation location)
    {
        Name = name;
        Kind = kind;
        Type = type;
        Storage = storage;
        Location = location;
    }

    public string Name { get; }
    public SymbolKind Kind { get; }
    public QualType Type { get; }
    public StorageClass Storage { get; }
    public SourceLocation Location { get; }

    /// <summary>For <see cref="SymbolKind.EnumConstant"/>, the constant's value.</summary>
    public long EnumValue { get; init; }

    /// <summary>True once a function/object with external or tentative linkage has a real definition.</summary>
    public bool IsDefined { get; set; }

    public override string ToString() => $"{Kind} {Name}: {Type}";
}
