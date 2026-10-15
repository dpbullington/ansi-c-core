using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Emit;

/// <summary>Thrown when the emitter meets a construct this (growing) vertical slice cannot yet translate.</summary>
public sealed class EmitException : Exception
{
    public EmitException(string message) : base(message) { }
}

/// <summary>
/// Maps resolved C types to the C# types the emitted code uses. The scalar types map to their
/// natural C# counterparts (so C#'s own promotions mostly mirror C's); pointers and aggregates
/// are handled against the runtime memory model in later slices.
/// </summary>
internal static class TypeMapper
{
    public static string Map(QualType type) => Map(type.Type);

    public static string Map(CType type) => type switch
    {
        VoidType => "void",
        EnumType => "int",
        IntegerType i => MapInteger(i),
        FloatingType f => MapFloating(f),
        _ => throw new EmitException($"type '{type.Display}' is not supported by the emitter yet"),
    };

    private static string MapInteger(IntegerType i) => i.Kind switch
    {
        IntegerKind.Char or IntegerKind.SignedChar => "sbyte",
        IntegerKind.UnsignedChar => "byte",
        IntegerKind.Short => "short",
        IntegerKind.UnsignedShort => "ushort",
        IntegerKind.Int => "int",
        IntegerKind.UnsignedInt => "uint",
        IntegerKind.Long => "long",
        IntegerKind.UnsignedLong => "ulong",
        _ => "int",
    };

    private static string MapFloating(FloatingType f) => f.Kind == FloatingKind.Float ? "float" : "double";
}
