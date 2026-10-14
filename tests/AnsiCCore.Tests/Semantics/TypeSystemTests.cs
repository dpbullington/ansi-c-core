using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Tests.Semantics;

[TestFixture]
public class TypeSystemTests
{
    // --- size / alignment -------------------------------------------------

    [Test]
    public void SizeOf_BasicTypes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TypeOperations.SizeOf(CTypes.Char), Is.EqualTo(1));
            Assert.That(TypeOperations.SizeOf(CTypes.Short), Is.EqualTo(2));
            Assert.That(TypeOperations.SizeOf(CTypes.Int), Is.EqualTo(4));
            Assert.That(TypeOperations.SizeOf(CTypes.Long), Is.EqualTo(8));
            Assert.That(TypeOperations.SizeOf(CTypes.Double), Is.EqualTo(8));
            Assert.That(TypeOperations.SizeOf(new PointerType(CTypes.Char)), Is.EqualTo(8));
        });
    }

    [Test]
    public void SizeOf_Array_IsElementCountTimesElementSize()
        => Assert.That(TypeOperations.SizeOf(new ArrayType(CTypes.Int, 10)), Is.EqualTo(40));

    [Test]
    public void SizeOf_IncompleteTypes_IsNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TypeOperations.SizeOf(CTypes.Void), Is.Null);
            Assert.That(TypeOperations.SizeOf(new ArrayType(CTypes.Int, null)), Is.Null);
            Assert.That(TypeOperations.SizeOf(new RecordType(isUnion: false, "Fwd")), Is.Null);
        });
    }

    // --- compatibility ----------------------------------------------------

    [Test]
    public void Compatibility_BasicTypes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TypeOperations.AreCompatible(CTypes.Int, CTypes.Int), Is.True);
            Assert.That(TypeOperations.AreCompatible(CTypes.Int, CTypes.Long), Is.False);
            Assert.That(TypeOperations.AreCompatible(CTypes.Char, CTypes.SignedChar), Is.False); // plain char is distinct
            Assert.That(TypeOperations.AreCompatible(new PointerType(CTypes.Int), new PointerType(CTypes.Int)), Is.True);
            Assert.That(TypeOperations.AreCompatible(new PointerType(CTypes.Int), new PointerType(CTypes.Char)), Is.False);
        });
    }

    [Test]
    public void Compatibility_EnumIsCompatibleWithInt()
        => Assert.That(TypeOperations.AreCompatible(new EnumType("E"), CTypes.Int), Is.True);

    [Test]
    public void Compatibility_DistinctRecordsAreIncompatible()
    {
        var a = new RecordType(false, "A");
        var b = new RecordType(false, "B");
        Assert.That(TypeOperations.AreCompatible(a, b), Is.False);
        Assert.That(TypeOperations.AreCompatible(a, a), Is.True);
    }

    // --- integer promotion ------------------------------------------------

    [Test]
    public void Promote_SmallIntegersBecomeInt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TypeOperations.Promote(CTypes.Char), Is.SameAs(CTypes.Int));
            Assert.That(TypeOperations.Promote(CTypes.Short), Is.SameAs(CTypes.Int));
            Assert.That(TypeOperations.Promote(CTypes.UnsignedShort), Is.SameAs(CTypes.Int));
            Assert.That(TypeOperations.Promote(new EnumType("E")), Is.SameAs(CTypes.Int));
            Assert.That(TypeOperations.Promote(CTypes.Int), Is.SameAs(CTypes.Int));
            Assert.That(TypeOperations.Promote(CTypes.UnsignedLong), Is.SameAs(CTypes.UnsignedLong));
        });
    }

    // --- usual arithmetic conversions -------------------------------------

    [Test]
    public void UsualArithmeticConversions_FollowC89Rules()
    {
        Assert.Multiple(() =>
        {
            // Small integers promote, then the result is int.
            Assert.That(TypeOperations.UsualArithmeticConversions(CTypes.Char, CTypes.Char), Is.SameAs(CTypes.Int));
            // Higher rank wins among same-signedness.
            Assert.That(TypeOperations.UsualArithmeticConversions(CTypes.Int, CTypes.Long), Is.SameAs(CTypes.Long));
            // Same rank, different sign → unsigned.
            Assert.That(TypeOperations.UsualArithmeticConversions(CTypes.Int, CTypes.UnsignedInt), Is.SameAs(CTypes.UnsignedInt));
            // signed long can represent all unsigned int → long.
            Assert.That(TypeOperations.UsualArithmeticConversions(CTypes.UnsignedInt, CTypes.Long), Is.SameAs(CTypes.Long));
            // unsigned long vs long → unsigned long.
            Assert.That(TypeOperations.UsualArithmeticConversions(CTypes.UnsignedLong, CTypes.Long), Is.SameAs(CTypes.UnsignedLong));
            // Floating always wins.
            Assert.That(TypeOperations.UsualArithmeticConversions(CTypes.Int, CTypes.Double), Is.SameAs(CTypes.Double));
            Assert.That(TypeOperations.UsualArithmeticConversions(CTypes.Float, CTypes.Double), Is.SameAs(CTypes.Double));
        });
    }
}
