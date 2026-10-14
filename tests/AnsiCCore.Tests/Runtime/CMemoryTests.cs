using AnsiCCore.Runtime.Memory;

namespace AnsiCCore.Tests.Runtime;

[TestFixture]
public class CMemoryTests
{
    private CMemory _mem = null!;

    [SetUp]
    public void SetUp() => _mem = new CMemory();

    // --- basic round-trips ------------------------------------------------

    [Test]
    public void ScalarRoundTrips_ForAllTypes()
    {
        var p = _mem.Malloc(8);
        Assert.Multiple(() =>
        {
            _mem.WriteInt32(p, -12345); Assert.That(_mem.ReadInt32(p), Is.EqualTo(-12345));
            _mem.WriteUInt32(p, 0xDEADBEEF); Assert.That(_mem.ReadUInt32(p), Is.EqualTo(0xDEADBEEF));
            _mem.WriteInt64(p, long.MinValue); Assert.That(_mem.ReadInt64(p), Is.EqualTo(long.MinValue));
            _mem.WriteInt16(p, -3); Assert.That(_mem.ReadInt16(p), Is.EqualTo((short)-3));
            _mem.WriteInt8(p, -1); Assert.That(_mem.ReadInt8(p), Is.EqualTo((sbyte)-1));
            _mem.WriteSingle(p, 3.5f); Assert.That(_mem.ReadSingle(p), Is.EqualTo(3.5f));
            _mem.WriteDouble(p, 2.718281828); Assert.That(_mem.ReadDouble(p), Is.EqualTo(2.718281828));
        });
    }

    [Test]
    public void FreshMalloc_IsZeroed()
    {
        var p = _mem.Malloc(4);
        Assert.That(_mem.ReadInt32(p), Is.EqualTo(0));
    }

    // --- pointer arithmetic & arrays --------------------------------------

    [Test]
    public void PointerArithmetic_IndexesElements()
    {
        var a = _mem.Malloc(10 * CSizes.Int); // int a[10]
        for (int i = 0; i < 10; i++)
            _mem.WriteInt32(a + (long)i * CSizes.Int, i * i);

        Assert.Multiple(() =>
        {
            Assert.That(_mem.ReadInt32(a + 3L * CSizes.Int), Is.EqualTo(9));
            Assert.That(_mem.ReadInt32(a + 9L * CSizes.Int), Is.EqualTo(81));
        });
    }

    [Test]
    public void PointerDifference_IsByteDistance()
    {
        var a = _mem.Malloc(40);
        var p = a + 20;
        Assert.That(CPtr.ByteDistance(p, a), Is.EqualTo(20));   // emitter divides by sizeof(element)
    }

    [Test]
    public void PointerComparison_Orders()
    {
        var a = _mem.Malloc(16);
        Assert.That(a < a + 4, Is.True);
        Assert.That((a + 8) > (a + 4), Is.True);
    }

    [Test]
    public void OnePastTheEndPointer_IsRepresentable_ButNotDereferenceable()
    {
        var a = _mem.Malloc(4 * CSizes.Int);
        var end = a + 4L * CSizes.Int; // &a[4] — legal to form
        Assert.That(end > a, Is.True);
        Assert.Throws<CMemoryException>(() => _mem.ReadInt32(end)); // but dereferencing faults
    }

    // --- faithful-to-C behaviors -----------------------------------------

    [Test]
    public void TypePunning_ReadIntBytesThroughChar()
    {
        var p = _mem.Malloc(4);
        _mem.WriteInt32(p, 0x04030201);
        // Little-endian: the low byte comes first, like a char* walking the int.
        Assert.Multiple(() =>
        {
            Assert.That(_mem.ReadUInt8(p + 0), Is.EqualTo(0x01));
            Assert.That(_mem.ReadUInt8(p + 1), Is.EqualTo(0x02));
            Assert.That(_mem.ReadUInt8(p + 2), Is.EqualTo(0x03));
            Assert.That(_mem.ReadUInt8(p + 3), Is.EqualTo(0x04));
        });
    }

    [Test]
    public void Union_FieldsShareStorage()
    {
        // union { int i; float f; } — writing one member is observable through the other.
        var u = _mem.Malloc(4);
        _mem.WriteSingle(u, 1.0f);
        Assert.That(_mem.ReadInt32(u), Is.EqualTo(0x3F800000)); // IEEE-754 bits of 1.0f
    }

    [Test]
    public void PointersCanBeStoredInMemory()
    {
        // int x; int *p = &x; int **pp = &p;  — a pointer round-trips through memory.
        var x = _mem.Malloc(CSizes.Int);
        _mem.WriteInt32(x, 42);

        var p = _mem.Malloc(CSizes.Pointer);
        _mem.WritePointer(p, x);

        var pp = _mem.Malloc(CSizes.Pointer);
        _mem.WritePointer(pp, p);

        var backToX = _mem.ReadPointer(_mem.ReadPointer(pp));
        Assert.That(_mem.ReadInt32(backToX), Is.EqualTo(42));
    }

    [Test]
    public void IntToPointerRoundTrip_PreservesAddress()
    {
        var p = _mem.Malloc(8) + 3;
        ulong asInt = p.Address;        // (uintptr_t)p
        var back = new CPtr(asInt);     // (void*)asInt
        Assert.That(back, Is.EqualTo(p));
    }

    // --- bulk operations --------------------------------------------------

    [Test]
    public void Copy_MovesBytes()
    {
        var src = _mem.Malloc(4);
        _mem.WriteInt32(src, 0x11223344);
        var dst = _mem.Malloc(4);
        _mem.Copy(dst, src, 4);
        Assert.That(_mem.ReadInt32(dst), Is.EqualTo(0x11223344));
    }

    [Test]
    public void Copy_HandlesOverlap()
    {
        var a = _mem.Malloc(8);
        for (byte i = 0; i < 8; i++) _mem.WriteUInt8(a + i, i);
        _mem.Copy(a + 2, a, 4); // memmove-style overlap
        Assert.Multiple(() =>
        {
            Assert.That(_mem.ReadUInt8(a + 2), Is.EqualTo(0));
            Assert.That(_mem.ReadUInt8(a + 5), Is.EqualTo(3));
        });
    }

    [Test]
    public void Set_FillsBytes()
    {
        var p = _mem.Malloc(4);
        _mem.Set(p, 0xAB, 4);
        Assert.That(_mem.ReadUInt32(p), Is.EqualTo(0xABABABAB));
    }

    [Test]
    public void Compare_OrdersLexicographically()
    {
        var a = _mem.AllocateStaticBytes("abc\0"u8);
        var b = _mem.AllocateStaticBytes("abd\0"u8);
        Assert.That(_mem.Compare(a, b, 4), Is.LessThan(0));
        Assert.That(_mem.Compare(a, a, 4), Is.EqualTo(0));
    }

    [Test]
    public void AllocateStaticBytes_InitializesContent()
    {
        var s = _mem.AllocateStaticBytes("Hi"u8);
        Assert.That(_mem.ReadUInt8(s), Is.EqualTo((byte)'H'));
        Assert.That(_mem.ReadUInt8(s + 1), Is.EqualTo((byte)'i'));
    }

    // --- allocation lifecycle & safety faults -----------------------------

    [Test]
    public void NullDereference_Faults()
        => Assert.Throws<CMemoryException>(() => _mem.ReadInt32(CPtr.Null));

    [Test]
    public void WildPointer_Faults()
        => Assert.Throws<CMemoryException>(() => _mem.ReadInt32(new CPtr(0x999999)));

    [Test]
    public void OutOfBounds_Faults()
    {
        var p = _mem.Malloc(4);
        Assert.Throws<CMemoryException>(() => _mem.ReadInt64(p)); // reads 8 from a 4-byte region
    }

    [Test]
    public void UseAfterFree_Faults()
    {
        var p = _mem.Malloc(4);
        _mem.Free(p);
        Assert.Throws<CMemoryException>(() => _mem.ReadInt32(p));
    }

    [Test]
    public void DoubleFree_Faults()
    {
        var p = _mem.Malloc(4);
        _mem.Free(p);
        Assert.Throws<CMemoryException>(() => _mem.Free(p));
    }

    [Test]
    public void FreeNull_IsNoOp()
        => Assert.DoesNotThrow(() => _mem.Free(CPtr.Null));

    [Test]
    public void FreeInteriorPointer_Faults()
    {
        var p = _mem.Malloc(8);
        Assert.Throws<CMemoryException>(() => _mem.Free(p + 2));
    }

    [Test]
    public void FreeAutomaticStorage_Faults()
    {
        var p = _mem.Allocate(4, RegionKind.Automatic);
        Assert.Throws<CMemoryException>(() => _mem.Free(p));
    }

    [Test]
    public void Realloc_PreservesContentsAndGrows()
    {
        var p = _mem.Malloc(4);
        _mem.WriteInt32(p, 0x55667788);
        var bigger = _mem.Realloc(p, 16);
        Assert.Multiple(() =>
        {
            Assert.That(_mem.ReadInt32(bigger), Is.EqualTo(0x55667788)); // old content preserved
            Assert.That(_mem.ReadInt32(bigger + 12), Is.EqualTo(0));     // new tail zeroed
            Assert.Throws<CMemoryException>(() => _mem.ReadInt32(p));    // old block freed
        });
    }

    [Test]
    public void Calloc_ZeroesAndSizes()
    {
        var p = _mem.Calloc(4, CSizes.Int);
        Assert.That(_mem.ReadInt32(p + 12), Is.EqualTo(0));
    }

    [Test]
    public void SeparateAllocations_DoNotAlias()
    {
        var a = _mem.Malloc(4);
        var b = _mem.Malloc(4);
        _mem.WriteInt32(a, 1);
        _mem.WriteInt32(b, 2);
        Assert.That(_mem.ReadInt32(a), Is.EqualTo(1));
        Assert.That(a.Address, Is.Not.EqualTo(b.Address));
    }
}
