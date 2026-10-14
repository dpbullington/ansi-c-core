using System.Globalization;

namespace AnsiCCore.Runtime.Memory;

/// <summary>
/// A C pointer in the safe memory model: a numeric address into the flat address space of a
/// <see cref="CMemory"/>. Address 0 is the null pointer. Arithmetic is in <em>bytes</em> — the
/// emitter multiplies by the element size — which keeps pointer casts a no-op and lets a
/// pointer be stored in memory as a plain 8-byte value.
/// </summary>
public readonly struct CPtr : IEquatable<CPtr>, IComparable<CPtr>
{
    public CPtr(ulong address) => Address = address;

    public ulong Address { get; }

    /// <summary>The null pointer (address 0).</summary>
    public static CPtr Null => default;

    public bool IsNull => Address == 0;

    // Byte arithmetic. The emitter scales by sizeof(element); these are raw byte offsets.
    public static CPtr operator +(CPtr p, long bytes) => new(unchecked((ulong)((long)p.Address + bytes)));
    public static CPtr operator -(CPtr p, long bytes) => new(unchecked((ulong)((long)p.Address - bytes)));

    /// <summary>The signed byte distance between two pointers (the emitter divides by the element size).</summary>
    public static long ByteDistance(CPtr a, CPtr b) => unchecked((long)a.Address - (long)b.Address);

    public static bool operator ==(CPtr a, CPtr b) => a.Address == b.Address;
    public static bool operator !=(CPtr a, CPtr b) => a.Address != b.Address;
    public static bool operator <(CPtr a, CPtr b) => a.Address < b.Address;
    public static bool operator >(CPtr a, CPtr b) => a.Address > b.Address;
    public static bool operator <=(CPtr a, CPtr b) => a.Address <= b.Address;
    public static bool operator >=(CPtr a, CPtr b) => a.Address >= b.Address;

    public bool Equals(CPtr other) => Address == other.Address;
    public override bool Equals(object? obj) => obj is CPtr p && Equals(p);
    public override int GetHashCode() => Address.GetHashCode();
    public int CompareTo(CPtr other) => Address.CompareTo(other.Address);

    public override string ToString() => IsNull ? "(null)" : "0x" + Address.ToString("x", CultureInfo.InvariantCulture);
}
