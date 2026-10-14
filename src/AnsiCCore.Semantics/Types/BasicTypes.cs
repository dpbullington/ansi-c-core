namespace AnsiCCore.Semantics.Types;

/// <summary>The distinct C89 integer types. Plain <c>char</c> is separate from the explicitly-signed forms.</summary>
public enum IntegerKind
{
    Char, SignedChar, UnsignedChar,
    Short, UnsignedShort,
    Int, UnsignedInt,
    Long, UnsignedLong,
}

/// <summary>
/// An integer type. Carries the properties the conversion rules need: signedness, integer
/// conversion <see cref="Rank"/>, and <see cref="Size"/> in bytes (from the target data model
/// in <see cref="CTypes"/>). Instances are canonical singletons.
/// </summary>
public sealed class IntegerType : CType
{
    internal IntegerType(IntegerKind kind, bool isSigned, int rank, int size, string display)
    {
        Kind = kind;
        IsSigned = isSigned;
        Rank = rank;
        Size = size;
        Display = display;
    }

    public IntegerKind Kind { get; }
    public bool IsSigned { get; }

    /// <summary>Integer conversion rank: char/schar/uchar=1, short=2, int=3, long=4.</summary>
    public int Rank { get; }
    public int Size { get; }
    public override string Display { get; }
}

/// <summary>The C89 floating types.</summary>
public enum FloatingKind { Float, Double, LongDouble }

/// <summary>A floating-point type. Canonical singleton.</summary>
public sealed class FloatingType : CType
{
    internal FloatingType(FloatingKind kind, int size, int rank, string display)
    {
        Kind = kind;
        Size = size;
        Rank = rank;
        Display = display;
    }

    public FloatingKind Kind { get; }
    public int Size { get; }

    /// <summary>Conversion rank among floating types: float=1, double=2, long double=3.</summary>
    public int Rank { get; }
    public override string Display { get; }
}
