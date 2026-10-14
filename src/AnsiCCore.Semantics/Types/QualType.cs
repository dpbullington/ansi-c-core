namespace AnsiCCore.Semantics.Types;

/// <summary>
/// A type together with its qualifiers — the way types appear in declarations, pointer
/// pointees, array elements, struct members, and function signatures. Modeled after Clang's
/// <c>QualType</c> so that canonical <see cref="CType"/> objects can be shared regardless of
/// the qualifiers applied at a particular use site.
/// </summary>
public readonly record struct QualType(CType Type, TypeQualifiers Qualifiers = TypeQualifiers.None)
{
    public bool IsConst => (Qualifiers & TypeQualifiers.Const) != 0;
    public bool IsVolatile => (Qualifiers & TypeQualifiers.Volatile) != 0;

    public QualType Unqualified => this with { Qualifiers = TypeQualifiers.None };
    public QualType With(TypeQualifiers extra) => this with { Qualifiers = Qualifiers | extra };

    /// <summary>A bare type is implicitly an unqualified <see cref="QualType"/>.</summary>
    public static implicit operator QualType(CType type) => new(type);

    public override string ToString()
    {
        string prefix = string.Empty;
        if (IsConst) prefix += "const ";
        if (IsVolatile) prefix += "volatile ";
        return prefix + Type.Display;
    }
}
