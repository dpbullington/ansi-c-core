namespace AnsiCCore.Tests.Emit;

/// <summary>End-to-end tests for struct bit-fields: read, write, packing, signedness, and compound ops.</summary>
[TestFixture]
public class EmitBitFieldTests
{
    private static void ExitsWith(int expected, string cSource)
        => Assert.That(EmitTestHost.Run(cSource), Is.EqualTo(expected), () => EmitTestHost.Transpile(cSource));

    [Test]
    public void ReadAndWriteBitFields()
    {
        const string src = """
            struct Flags { unsigned a : 3; unsigned b : 4; unsigned c : 1; };
            int main(void) {
                struct Flags f;
                f.a = 5; f.b = 9; f.c = 1;
                return f.a + f.b + f.c; /* 5 + 9 + 1 = 15 */
            }
            """;
        ExitsWith(15, src);
    }

    [Test]
    public void BitFieldTruncatesToWidth()
    {
        // a is 3 bits: assigning 13 (0b1101) keeps the low 3 bits (0b101 = 5).
        const string src = """
            struct S { unsigned a : 3; };
            int main(void) { struct S s; s.a = 13; return s.a; }
            """;
        ExitsWith(5, src);
    }

    [Test]
    public void AdjacentBitFieldsAreIndependent()
    {
        // Writing b must not disturb a (they share one storage unit).
        const string src = """
            struct S { unsigned a : 4; unsigned b : 4; };
            int main(void) { struct S s; s.a = 15; s.b = 0; s.b = 7; return s.a * 100 + s.b; }
            """;
        ExitsWith(1507, src);
    }

    [Test]
    public void SignedBitFieldSignExtends()
    {
        // A 3-bit signed field holding 0b111 reads back as -1.
        const string src = """
            struct S { int x : 3; };
            int main(void) { struct S s; s.x = 7; return s.x; }
            """;
        ExitsWith(-1, src);
    }

    [Test]
    public void BitFieldPackingKeepsStructSmall()
    {
        // Eight 1-bit fields pack into a single 4-byte int unit.
        const string src = """
            struct Bits { unsigned b0:1; unsigned b1:1; unsigned b2:1; unsigned b3:1;
                          unsigned b4:1; unsigned b5:1; unsigned b6:1; unsigned b7:1; };
            int main(void) { return (int)sizeof(struct Bits); }
            """;
        ExitsWith(4, src);
    }

    [Test]
    public void BitFieldCompoundAssignment()
    {
        const string src = """
            struct S { unsigned n : 5; };
            int main(void) { struct S s; s.n = 10; s.n += 3; return s.n; }
            """;
        ExitsWith(13, src);
    }

    [Test]
    public void BitFieldIncrement()
    {
        const string src = """
            struct S { unsigned n : 4; };
            int main(void) { struct S s; s.n = 14; s.n++; s.n++; return s.n; } /* 14->15->0 (wraps at 4 bits) */
            """;
        ExitsWith(0, src);
    }

    [Test]
    public void BitFieldsAlongsideRegularMembers()
    {
        const string src = """
            struct Packet { unsigned version : 4; unsigned type : 4; int payload; };
            int main(void) {
                struct Packet p;
                p.version = 2; p.type = 7; p.payload = 1000;
                return p.version + p.type + p.payload; /* 2 + 7 + 1000 */
            }
            """;
        ExitsWith(1009, src);
    }
}
