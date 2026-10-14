namespace AnsiCCore.Runtime.Memory;

/// <summary>
/// The sizes (in bytes) of the C scalar types in the runtime's data model. This is the
/// <em>runtime half</em> of the size contract with generated code and must stay in lockstep
/// with the compiler's model (LP64): the emitter scales pointer arithmetic and lays out
/// aggregates using the same numbers.
/// </summary>
public static class CSizes
{
    public const int Char = 1;
    public const int Short = 2;
    public const int Int = 4;
    public const int Long = 8;
    public const int Float = 4;
    public const int Double = 8;
    public const int Pointer = 8;
}
