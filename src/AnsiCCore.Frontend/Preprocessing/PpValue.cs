namespace AnsiCCore.Frontend.Preprocessing;

/// <summary>
/// A value in a <c>#if</c> constant expression. C89 evaluates preprocessor arithmetic in the
/// widest integer types — <c>long</c> / <c>unsigned long</c> — which we model as 64-bit bits
/// plus an <see cref="IsUnsigned"/> flag. Bits are reinterpreted as <see cref="ulong"/> when
/// unsigned.
/// </summary>
internal readonly record struct PpValue(long Bits, bool IsUnsigned)
{
    public static readonly PpValue Zero = new(0, false);
    public static readonly PpValue One = new(1, false);

    public static PpValue Signed(long v) => new(v, false);
    public static PpValue Unsigned(ulong v) => new(unchecked((long)v), true);
    public static PpValue Bool(bool b) => b ? One : Zero;

    public ulong U => unchecked((ulong)Bits);
    public bool IsTrue => IsUnsigned ? U != 0 : Bits != 0;
}
