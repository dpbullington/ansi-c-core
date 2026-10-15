namespace AnsiCCore.Tests.Emit;

/// <summary>
/// End-to-end tests for the memory-model slice: pointers, address-taken locals, arrays,
/// structs/unions, malloc/free, string literals, and the stdio output shim. Each compiles and
/// runs the transpiled program and checks its exit code or stdout.
/// </summary>
[TestFixture]
public class EmitMemoryTests
{
    private static void ExitsWith(int expected, string cSource)
        => Assert.That(EmitTestHost.Run(cSource), Is.EqualTo(expected), () => EmitTestHost.Transpile(cSource));

    private static void Prints(string expected, string cSource)
        => Assert.That(EmitTestHost.RunWithOutput(cSource).Output, Is.EqualTo(expected), () => EmitTestHost.Transpile(cSource));

    // --- address-taken locals & pointers ---------------------------------

    [Test]
    public void AddressOfLocal_AndDereference()
        => ExitsWith(42, "int main(void) { int x = 0; int *p = &x; *p = 42; return x; }");

    [Test]
    public void PointerWritesAreVisibleThroughOriginal()
        => ExitsWith(15, "int main(void) { int x = 10; int *p = &x; *p = *p + 5; return x; }");

    [Test]
    public void PointerToPointer()
        => ExitsWith(7, "int main(void) { int x = 7; int *p = &x; int **pp = &p; return **pp; }");

    [Test]
    public void SwapViaPointers()
    {
        const string src = """
            void swap(int *a, int *b) { int t = *a; *a = *b; *b = t; }
            int main(void) { int x = 3; int y = 8; swap(&x, &y); return x * 10 + y; }
            """;
        ExitsWith(83, src);
    }

    [Test]
    public void NullPointerCheck()
        => ExitsWith(1, "int main(void) { int *p = 0; if (p == 0) return 1; return 0; }");

    // --- arrays -----------------------------------------------------------

    [Test]
    public void ArrayIndexingAndSum()
        => ExitsWith(60, "int main(void) { int a[3]; a[0] = 10; a[1] = 20; a[2] = 30; return a[0] + a[1] + a[2]; }");

    [Test]
    public void ArrayInitializerList()
        => ExitsWith(6, "int main(void) { int a[3] = { 1, 2, 3 }; return a[0] + a[1] + a[2]; }");

    [Test]
    public void PointerWalksArray()
    {
        const string src = """
            int main(void) {
                int a[4] = { 5, 6, 7, 8 };
                int *p = a;
                int sum = 0;
                int i;
                for (i = 0; i < 4; i = i + 1) { sum = sum + *p; p = p + 1; }
                return sum;
            }
            """;
        ExitsWith(26, src);
    }

    [Test]
    public void ArrayDecayAndPointerDifference()
        => ExitsWith(3, "int main(void) { int a[8]; int *p = &a[3]; return (int)(p - a); }");

    // --- structs / unions -------------------------------------------------

    [Test]
    public void StructMemberAccess()
    {
        const string src = """
            struct Point { int x; int y; };
            int main(void) { struct Point p; p.x = 3; p.y = 4; return p.x * p.x + p.y * p.y; }
            """;
        ExitsWith(25, src);
    }

    [Test]
    public void StructPointerArrow()
    {
        const string src = """
            struct Point { int x; int y; };
            void move(struct Point *p, int dx, int dy) { p->x = p->x + dx; p->y = p->y + dy; }
            int main(void) { struct Point p; p.x = 1; p.y = 1; move(&p, 4, 5); return p.x * 10 + p.y; }
            """;
        ExitsWith(56, src);
    }

    [Test]
    public void StructInitializerList()
    {
        const string src = """
            struct Rect { int w; int h; };
            int main(void) { struct Rect r = { 6, 7 }; return r.w * r.h; }
            """;
        ExitsWith(42, src);
    }

    [Test]
    public void UnionSharesStorage()
    {
        const string src = """
            union U { int i; unsigned char b[4]; };
            int main(void) { union U u; u.i = 0; u.b[0] = 1; return u.i; }
            """;
        ExitsWith(1, src); // little-endian: low byte set to 1
    }

    [Test]
    public void StructAssignmentCopies()
    {
        const string src = """
            struct P { int x; int y; };
            int main(void) { struct P a; a.x = 2; a.y = 9; struct P b; b = a; b.x = 100; return a.x * 10 + b.x; }
            """;
        ExitsWith(120, src); // a.x unchanged (20) + b.x (100)
    }

    // --- malloc / free ----------------------------------------------------

    [Test]
    public void MallocAndFree()
    {
        const string src = """
            int main(void) {
                int *p = malloc(3 * sizeof(int));
                p[0] = 11; p[1] = 22; p[2] = 33;
                int sum = p[0] + p[1] + p[2];
                free(p);
                return sum;
            }
            """;
        ExitsWith(66, src);
    }

    // --- strings & output -------------------------------------------------

    [Test]
    public void PutsWritesLine()
        => Prints("hello\n", "int main(void) { puts(\"hello\"); return 0; }");

    [Test]
    public void PrintfFormats()
        => Prints("x=7 c=A s=hi 100%\n",
            "int main(void) { printf(\"x=%d c=%c s=%s %d%%\\n\", 7, 65, \"hi\", 100); return 0; }");

    [Test]
    public void PutcharLoop()
        => Prints("ABC", "int main(void) { int c; for (c = 65; c < 68; c = c + 1) putchar(c); return 0; }");

    [Test]
    public void StringLiteralAndStrlen()
        => ExitsWith(5, "int main(void) { char *s = \"hello\"; return strlen(s); }");

    [Test]
    public void StrcmpEqualStrings()
        => ExitsWith(0, "int main(void) { return strcmp(\"abc\", \"abc\"); }");

    [Test]
    public void CharArrayFromStringLiteral()
        => ExitsWith((int)'e', "int main(void) { char s[6] = \"hello\"; return s[1]; }");

    // --- globals in memory ------------------------------------------------

    [Test]
    public void GlobalArray()
    {
        const string src = """
            int data[4] = { 2, 4, 6, 8 };
            int sum(void) { int s = 0; int i; for (i = 0; i < 4; i = i + 1) s = s + data[i]; return s; }
            int main(void) { return sum(); }
            """;
        ExitsWith(20, src);
    }
}
