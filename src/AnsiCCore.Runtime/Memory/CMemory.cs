using System.Buffers.Binary;

namespace AnsiCCore.Runtime.Memory;

/// <summary>Where a region came from — it governs what <c>free</c> will accept.</summary>
public enum RegionKind
{
    /// <summary>From <c>malloc</c>/<c>calloc</c>/<c>realloc</c>; the only kind <c>free</c> accepts.</summary>
    Heap,
    /// <summary>Automatic storage: an address-taken local or a compound temporary.</summary>
    Automatic,
    /// <summary>Static storage: file-scope objects, string literals, other constant data.</summary>
    Static,
}

/// <summary>
/// The safe analog of a C process's memory. Addressable objects are byte regions placed in a
/// single flat address space; a <see cref="CPtr"/> is an address into it. Scalar reads/writes
/// encode/decode little-endian bytes, so type punning, unions, pointers stored in memory, and
/// int&lt;-&gt;pointer casts all behave as in C — while every access is bounds- and
/// use-after-free-checked, turning C's undefined behavior into a catchable
/// <see cref="CMemoryException"/> instead of silent corruption.
/// </summary>
/// <remarks>
/// This is the runtime contract that emitted C# targets. An instance is one address space;
/// a transpiled program uses one. Regions are separated by guard gaps so a one-past-the-end
/// pointer is representable (legal in C) but dereferencing it faults.
/// </remarks>
public sealed class CMemory
{
    private sealed class MemoryBlock(ulong baseAddress, byte[] bytes, RegionKind kind)
    {
        public ulong Base { get; } = baseAddress;
        public byte[] Bytes { get; set; } = bytes;
        public RegionKind Kind { get; } = kind;
        public bool Freed { get; set; }
        public ulong End => Base + (ulong)Bytes.Length;
    }

    private const ulong Alignment = 16;
    private const ulong GuardGap = 64;

    // Blocks kept sorted by Base (allocations are monotonic, so new blocks append).
    private readonly List<MemoryBlock> _blocks = [];
    private readonly Dictionary<ulong, MemoryBlock> _byBase = [];
    private ulong _next = 0x1000; // leave the low page unmapped so null and small addresses fault

    // --- allocation -------------------------------------------------------

    /// <summary>Allocates a zeroed region of the given kind and returns a pointer to its start.</summary>
    public CPtr Allocate(long size, RegionKind kind)
    {
        if (size < 0) throw new CMemoryException($"negative allocation size {size}");

        var block = new MemoryBlock(_next, new byte[size], kind);
        _blocks.Add(block);
        _byBase[block.Base] = block;
        _next = AlignUp(block.Base + (ulong)size + GuardGap, Alignment);
        return new CPtr(block.Base);
    }

    /// <summary>Allocates a static region initialized from <paramref name="data"/> (e.g. a string literal).</summary>
    public CPtr AllocateStaticBytes(ReadOnlySpan<byte> data)
    {
        var ptr = Allocate(data.Length, RegionKind.Static);
        data.CopyTo(ResolveSpan(ptr.Address, data.Length, "initialize"));
        return ptr;
    }

    // C standard-library heap facade.
    public CPtr Malloc(long size) => Allocate(size, RegionKind.Heap);

    public CPtr Calloc(long count, long size) => Allocate(checked(count * size), RegionKind.Heap);

    public CPtr Realloc(CPtr ptr, long newSize)
    {
        if (ptr.IsNull) return Malloc(newSize);
        if (!_byBase.TryGetValue(ptr.Address, out var old) || old.Freed || old.Kind != RegionKind.Heap)
            throw new CMemoryException($"realloc of invalid pointer {ptr}");

        var result = Malloc(newSize);
        int copy = (int)Math.Min(newSize, old.Bytes.Length);
        Array.Copy(old.Bytes, 0, _byBase[result.Address].Bytes, 0, copy);
        old.Freed = true;
        return result;
    }

    public void Free(CPtr ptr)
    {
        if (ptr.IsNull) return; // free(NULL) is a no-op
        if (!_byBase.TryGetValue(ptr.Address, out var block))
            throw new CMemoryException($"free of {ptr}, which was not returned by malloc");
        if (block.Kind != RegionKind.Heap)
            throw new CMemoryException($"free of non-heap pointer {ptr}");
        if (block.Freed)
            throw new CMemoryException($"double free of {ptr}");
        block.Freed = true;
    }

    // --- address resolution ----------------------------------------------

    private (MemoryBlock Block, int Offset) Resolve(ulong address, long size, string op)
    {
        if (address == 0)
            throw new CMemoryException($"null pointer {op}");

        var block = FindBlock(address);
        if (block is null)
            throw new CMemoryException($"wild pointer {op} at 0x{address:x}");
        if (block.Freed)
            throw new CMemoryException($"use-after-free: {op} at 0x{address:x}");
        if (address + (ulong)size > block.End)
            throw new CMemoryException($"out-of-bounds {op} of {size} byte(s) at 0x{address:x}");

        return (block, (int)(address - block.Base));
    }

    private Span<byte> ResolveSpan(ulong address, long size, string op)
    {
        var (block, offset) = Resolve(address, size, op);
        return block.Bytes.AsSpan(offset, (int)size);
    }

    /// <summary>Finds the region whose range contains <paramref name="address"/> (binary search by base).</summary>
    private MemoryBlock? FindBlock(ulong address)
    {
        int lo = 0, hi = _blocks.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            if (_blocks[mid].Base <= address) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        if (found < 0) return null;
        var block = _blocks[found];
        return address < block.End ? block : null;
    }

    private static ulong AlignUp(ulong value, ulong alignment) => (value + alignment - 1) / alignment * alignment;

    // --- scalar reads -----------------------------------------------------

    public sbyte ReadInt8(CPtr p) { var (b, o) = Resolve(p.Address, 1, "read"); return unchecked((sbyte)b.Bytes[o]); }
    public byte ReadUInt8(CPtr p) { var (b, o) = Resolve(p.Address, 1, "read"); return b.Bytes[o]; }
    public short ReadInt16(CPtr p) => BinaryPrimitives.ReadInt16LittleEndian(ResolveSpan(p.Address, 2, "read"));
    public ushort ReadUInt16(CPtr p) => BinaryPrimitives.ReadUInt16LittleEndian(ResolveSpan(p.Address, 2, "read"));
    public int ReadInt32(CPtr p) => BinaryPrimitives.ReadInt32LittleEndian(ResolveSpan(p.Address, 4, "read"));
    public uint ReadUInt32(CPtr p) => BinaryPrimitives.ReadUInt32LittleEndian(ResolveSpan(p.Address, 4, "read"));
    public long ReadInt64(CPtr p) => BinaryPrimitives.ReadInt64LittleEndian(ResolveSpan(p.Address, 8, "read"));
    public ulong ReadUInt64(CPtr p) => BinaryPrimitives.ReadUInt64LittleEndian(ResolveSpan(p.Address, 8, "read"));
    public float ReadSingle(CPtr p) => BinaryPrimitives.ReadSingleLittleEndian(ResolveSpan(p.Address, 4, "read"));
    public double ReadDouble(CPtr p) => BinaryPrimitives.ReadDoubleLittleEndian(ResolveSpan(p.Address, 8, "read"));
    public CPtr ReadPointer(CPtr p) => new(ReadUInt64(p));

    // --- scalar writes ----------------------------------------------------

    public void WriteInt8(CPtr p, sbyte v) { var (b, o) = Resolve(p.Address, 1, "write"); b.Bytes[o] = unchecked((byte)v); }
    public void WriteUInt8(CPtr p, byte v) { var (b, o) = Resolve(p.Address, 1, "write"); b.Bytes[o] = v; }
    public void WriteInt16(CPtr p, short v) => BinaryPrimitives.WriteInt16LittleEndian(ResolveSpan(p.Address, 2, "write"), v);
    public void WriteUInt16(CPtr p, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(ResolveSpan(p.Address, 2, "write"), v);
    public void WriteInt32(CPtr p, int v) => BinaryPrimitives.WriteInt32LittleEndian(ResolveSpan(p.Address, 4, "write"), v);
    public void WriteUInt32(CPtr p, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(ResolveSpan(p.Address, 4, "write"), v);
    public void WriteInt64(CPtr p, long v) => BinaryPrimitives.WriteInt64LittleEndian(ResolveSpan(p.Address, 8, "write"), v);
    public void WriteUInt64(CPtr p, ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(ResolveSpan(p.Address, 8, "write"), v);
    public void WriteSingle(CPtr p, float v) => BinaryPrimitives.WriteSingleLittleEndian(ResolveSpan(p.Address, 4, "write"), v);
    public void WriteDouble(CPtr p, double v) => BinaryPrimitives.WriteDoubleLittleEndian(ResolveSpan(p.Address, 8, "write"), v);
    public void WritePointer(CPtr p, CPtr value) => WriteUInt64(p, value.Address);

    // --- bulk operations (memcpy / memmove / memset / memcmp) -------------

    /// <summary>Copies <paramref name="count"/> bytes; safe even when the regions overlap (like memmove).</summary>
    public void Copy(CPtr destination, CPtr source, long count)
    {
        if (count <= 0) return;
        var (dst, dstOff) = Resolve(destination.Address, count, "memcpy");
        var (src, srcOff) = Resolve(source.Address, count, "memcpy");
        Array.Copy(src.Bytes, srcOff, dst.Bytes, dstOff, count);
    }

    public void Set(CPtr destination, byte value, long count)
    {
        if (count <= 0) return;
        ResolveSpan(destination.Address, count, "memset").Fill(value);
    }

    public int Compare(CPtr a, CPtr b, long count)
    {
        if (count <= 0) return 0;
        var left = ResolveSpan(a.Address, count, "memcmp");
        var right = ResolveSpan(b.Address, count, "memcmp");
        return left.SequenceCompareTo(right);
    }
}
