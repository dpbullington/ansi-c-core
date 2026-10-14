namespace AnsiCCore.Semantics.Types;

/// <summary>
/// The canonical singleton instances of the basic C types, plus the target data model.
/// </summary>
/// <remarks>
/// Sizes follow a common LP64 model: <c>char</c> 1, <c>short</c> 2, <c>int</c> 4, <c>long</c> 8,
/// pointer 8, <c>float</c> 4, <c>double</c> 8. <c>long double</c> is modeled as 8 (it maps to
/// .NET <c>double</c> in emitted code); revisit alongside the runtime memory model.
/// </remarks>
public static class CTypes
{
    public static VoidType Void { get; } = new();

    /// <summary>The error sentinel (see <see cref="ErrorType"/>).</summary>
    public static ErrorType Error { get; } = new();

    // Integer types (kind, isSigned, rank, size).
    public static IntegerType Char { get; } = new(IntegerKind.Char, isSigned: true, rank: 1, size: 1, "char");
    public static IntegerType SignedChar { get; } = new(IntegerKind.SignedChar, true, 1, 1, "signed char");
    public static IntegerType UnsignedChar { get; } = new(IntegerKind.UnsignedChar, false, 1, 1, "unsigned char");
    public static IntegerType Short { get; } = new(IntegerKind.Short, true, 2, 2, "short");
    public static IntegerType UnsignedShort { get; } = new(IntegerKind.UnsignedShort, false, 2, 2, "unsigned short");
    public static IntegerType Int { get; } = new(IntegerKind.Int, true, 3, 4, "int");
    public static IntegerType UnsignedInt { get; } = new(IntegerKind.UnsignedInt, false, 3, 4, "unsigned int");
    public static IntegerType Long { get; } = new(IntegerKind.Long, true, 4, 8, "long");
    public static IntegerType UnsignedLong { get; } = new(IntegerKind.UnsignedLong, false, 4, 8, "unsigned long");

    // Floating types.
    public static FloatingType Float { get; } = new(FloatingKind.Float, size: 4, rank: 1, "float");
    public static FloatingType Double { get; } = new(FloatingKind.Double, 8, 2, "double");
    public static FloatingType LongDouble { get; } = new(FloatingKind.LongDouble, 8, 3, "long double");

    /// <summary>Size of any pointer, in bytes (LP64).</summary>
    public const int PointerSize = 8;

    /// <summary>The unsigned integer counterpart of a signed integer type (used by the conversion rules).</summary>
    public static IntegerType ToUnsigned(IntegerType t) => t.Kind switch
    {
        IntegerKind.Char or IntegerKind.SignedChar => UnsignedChar,
        IntegerKind.Short => UnsignedShort,
        IntegerKind.Int => UnsignedInt,
        IntegerKind.Long => UnsignedLong,
        _ => t, // already unsigned
    };
}
