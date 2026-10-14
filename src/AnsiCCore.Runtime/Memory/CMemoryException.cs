namespace AnsiCCore.Runtime.Memory;

/// <summary>
/// Raised when generated code performs a memory operation that C would leave undefined — a
/// null or out-of-bounds dereference, use-after-free, or an invalid <c>free</c>. Instead of
/// corrupting state (as native UB would), the safe model turns the fault into this catchable
/// exception.
/// </summary>
public sealed class CMemoryException : Exception
{
    public CMemoryException(string message) : base(message) { }
}
