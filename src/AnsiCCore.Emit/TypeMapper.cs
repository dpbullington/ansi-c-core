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
        PointerType p => MapPointer(p),
        // A struct/union passed or returned by value is carried as a CPtr to its storage.
        RecordType => "CPtr",
        _ => throw new EmitException($"type '{type.Display}' is not supported by the emitter yet"),
    };

    /// <summary>True for a pointer-to-function type, which maps to a C# delegate rather than CPtr.</summary>
    public static bool IsFunctionPointer(CType type) => type is PointerType { Pointee.Type: FunctionType };

    private static string MapPointer(PointerType p)
    {
        if (p.Pointee.Type is not FunctionType ft)
            return "CPtr";

        // Function pointer -> Func<...>/Action<...> over the mapped signature.
        string ret = Map(ft.ReturnType);
        var paramTypes = ft.Parameters.Select(x => Map(x.Type)).ToList();
        if (ret == "void")
            return paramTypes.Count == 0 ? "System.Action" : $"System.Action<{string.Join(", ", paramTypes)}>";
        paramTypes.Add(ret);
        return $"System.Func<{string.Join(", ", paramTypes)}>";
    }

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

    // long double maps to double: .NET has no binary float wider than 64 bits, so long double has
    // the same representation as double — a conforming C89 choice (as on MSVC, ARM64, and others),
    // consistent with the type system modeling sizeof(long double) == 8.
    private static string MapFloating(FloatingType f) => f.Kind == FloatingKind.Float ? "float" : "double";
}
