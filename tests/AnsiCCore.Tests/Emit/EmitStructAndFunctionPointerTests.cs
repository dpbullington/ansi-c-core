namespace AnsiCCore.Tests.Emit;

/// <summary>
/// End-to-end tests for the gap-closing slice: struct pass/return by value and function
/// pointers (lowered to C# delegates).
/// </summary>
[TestFixture]
public class EmitStructAndFunctionPointerTests
{
    private static void ExitsWith(int expected, string cSource)
        => Assert.That(EmitTestHost.Run(cSource), Is.EqualTo(expected), () => EmitTestHost.Transpile(cSource));

    // --- struct by value --------------------------------------------------

    [Test]
    public void StructPassedByValue_IsCopied()
    {
        const string src = """
            struct P { int x; int y; };
            int sum(struct P p) { p.x = 100; return p.x + p.y; }
            int main(void) { struct P a; a.x = 3; a.y = 4; int s = sum(a); return s + a.x; }
            """;
        // sum sees x=100,y=4 -> 104; the caller's a.x is unchanged (3): 104 + 3 = 107.
        ExitsWith(107, src);
    }

    [Test]
    public void StructReturnedByValue()
    {
        const string src = """
            struct P { int x; int y; };
            struct P make(int a, int b) { struct P p; p.x = a; p.y = b; return p; }
            int main(void) { struct P p = make(6, 7); return p.x * p.y; }
            """;
        ExitsWith(42, src);
    }

    [Test]
    public void StructReturnedByValue_MemberAccessedDirectly()
    {
        const string src = """
            struct P { int x; int y; };
            struct P make(void) { struct P p; p.x = 9; p.y = 5; return p; }
            int main(void) { return make().x - make().y; }
            """;
        ExitsWith(4, src);
    }

    [Test]
    public void StructRoundTripsThroughFunctions()
    {
        const string src = """
            struct Vec { int a; int b; int c; };
            struct Vec scale(struct Vec v, int k) { v.a = v.a * k; v.b = v.b * k; v.c = v.c * k; return v; }
            int main(void) {
                struct Vec v; v.a = 1; v.b = 2; v.c = 3;
                struct Vec w = scale(v, 10);
                return w.a + w.b + w.c + v.a; /* 10+20+30 + original 1 = 61 */
            }
            """;
        ExitsWith(61, src);
    }

    // --- function pointers ------------------------------------------------

    [Test]
    public void FunctionPointer_CalledThroughVariable()
    {
        const string src = """
            int add(int a, int b) { return a + b; }
            int main(void) { int (*op)(int, int) = add; return op(3, 4); }
            """;
        ExitsWith(7, src);
    }

    [Test]
    public void FunctionPointer_AddressOfAndDerefCall()
    {
        const string src = """
            int square(int n) { return n * n; }
            int main(void) { int (*f)(int) = &square; return (*f)(9); }
            """;
        ExitsWith(81, src);
    }

    [Test]
    public void FunctionPointer_AsCallbackParameter()
    {
        const string src = """
            int apply(int (*f)(int), int x) { return f(x); }
            int dbl(int n) { return n + n; }
            int main(void) { return apply(dbl, 21); }
            """;
        ExitsWith(42, src);
    }

    [Test]
    public void FunctionPointer_SelectedAtRuntime()
    {
        const string src = """
            int add(int a, int b) { return a + b; }
            int mul(int a, int b) { return a * b; }
            int main(void) {
                int (*op)(int, int);
                int which = 1;
                if (which) op = mul; else op = add;
                return op(6, 7);
            }
            """;
        ExitsWith(42, src);
    }

    [Test]
    public void FunctionPointer_NullCheck()
    {
        const string src = """
            int main(void) {
                int (*op)(int) = 0;
                if (op == 0) return 1;
                return 0;
            }
            """;
        ExitsWith(1, src);
    }
}
