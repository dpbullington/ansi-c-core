using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Emit;

/// <summary>
/// Maps a C scalar/pointer type to the <c>CMemory</c> accessor suffix used to read, write, or
/// store it (e.g. <c>int</c> → <c>Int32</c>, giving <c>ReadInt32</c>/<c>WriteInt32</c>/<c>StoreInt32</c>).
/// </summary>
internal static class MemoryAccess
{
    public static string Suffix(CType type) => type switch
    {
        IntegerType i => i.Kind switch
        {
            IntegerKind.Char or IntegerKind.SignedChar => "Int8",
            IntegerKind.UnsignedChar => "UInt8",
            IntegerKind.Short => "Int16",
            IntegerKind.UnsignedShort => "UInt16",
            IntegerKind.Int => "Int32",
            IntegerKind.UnsignedInt => "UInt32",
            IntegerKind.Long => "Int64",
            IntegerKind.UnsignedLong => "UInt64",
            _ => "Int32",
        },
        EnumType => "Int32",
        FloatingType { Kind: FloatingKind.Float } => "Single",
        FloatingType => "Double",
        PointerType { Pointee.Type: FunctionType } => throw new EmitException("function pointers stored in memory are not supported yet"),
        PointerType => "Pointer",
        _ => throw new EmitException($"type '{type.Display}' has no scalar memory accessor"),
    };

    public static long SizeOf(CType type)
        => TypeOperations.SizeOf(type) ?? throw new EmitException($"type '{type.Display}' has no known size");
}
