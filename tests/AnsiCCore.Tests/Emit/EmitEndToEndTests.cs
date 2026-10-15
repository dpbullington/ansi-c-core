namespace AnsiCCore.Tests.Emit;

/// <summary>
/// Compiles-and-runs transpiled C programs and asserts on the exit code (C main's return value).
/// This is the "final boss" style of test for the vertical slice: real behavior, end to end.
/// </summary>
[TestFixture]
public class EmitEndToEndTests
{
    private static void ExitsWith(int expected, string cSource)
        => Assert.That(EmitTestHost.Run(cSource), Is.EqualTo(expected), () => EmitTestHost.Transpile(cSource));

    [Test]
    public void ReturnsConstant()
        => ExitsWith(42, "int main(void) { return 42; }");

    [Test]
    public void ArithmeticPrecedence()
        => ExitsWith(14, "int main(void) { return 2 + 3 * 4; }");

    [Test]
    public void LocalsAndAssignment()
        => ExitsWith(30, "int main(void) { int a = 10; int b = 20; int c = a + b; return c; }");

    [Test]
    public void IntegerDivisionAndModulo()
        => ExitsWith(5, "int main(void) { int q = 17 / 7; int r = 17 % 7; return q + r; }"); // 2 + 3 = 5

    [Test]
    public void OctalLiteral_IsDecodedCorrectly()
        => ExitsWith(8, "int main(void) { return 010; }"); // octal 010 == 8

    [Test]
    public void CharacterConstant_HasIntValue()
        => ExitsWith(65, "int main(void) { return 'A'; }");

    [Test]
    public void IfElse_SelectsBranch()
        => ExitsWith(1, "int main(void) { int x = 5; if (x > 3) return 1; else return 0; }");

    [Test]
    public void LogicalOperators_YieldIntResults()
        => ExitsWith(1, "int main(void) { int a = 1; int b = 0; return (a || b) && !b; }");

    [Test]
    public void TernaryOperator()
        => ExitsWith(99, "int main(void) { int x = 7; return x == 7 ? 99 : 0; }");

    [Test]
    public void WhileLoop_SumsToN()
        => ExitsWith(55, "int main(void) { int i = 1; int sum = 0; while (i <= 10) { sum = sum + i; i = i + 1; } return sum; }");

    [Test]
    public void ForLoop_ComputesFactorial()
        => ExitsWith(120, "int main(void) { int f = 1; int i; for (i = 1; i <= 5; i = i + 1) f = f * i; return f; }");

    [Test]
    public void DoWhileLoop_RunsAtLeastOnce()
        => ExitsWith(5, "int main(void) { int i = 0; do { i = i + 1; } while (i < 5); return i; }");

    [Test]
    public void BreakAndContinue()
        => ExitsWith(2 + 4 + 6 + 8, "int main(void) { int i; int s = 0; for (i = 0; i < 100; i = i + 1) { if (i % 2) continue; if (i > 9) break; s = s + i; } return s; }"); // 0+2+4+6+8 = 20

    [Test]
    public void FunctionCall()
        => ExitsWith(7, "int add(int a, int b) { return a + b; } int main(void) { return add(3, 4); }");

    [Test]
    public void Recursion_Factorial()
        => ExitsWith(120, "int fact(int n) { if (n <= 1) return 1; return n * fact(n - 1); } int main(void) { return fact(5); }");

    [Test]
    public void Recursion_Fibonacci()
        => ExitsWith(55, "int fib(int n) { if (n < 2) return n; return fib(n - 1) + fib(n - 2); } int main(void) { return fib(10); }");

    [Test]
    public void MutualRecursion()
    {
        const string src = """
            int is_even(int n) { if (n == 0) return 1; return is_odd(n - 1); }
            int is_odd(int n) { if (n == 0) return 0; return is_even(n - 1); }
            int main(void) { return is_even(10); }
            """;
        ExitsWith(1, src);
    }

    [Test]
    public void GlobalVariable_IsShared()
    {
        const string src = """
            int counter = 100;
            int bump(void) { counter = counter + 1; return counter; }
            int main(void) { bump(); bump(); return counter; }
            """;
        ExitsWith(102, src);
    }

    [Test]
    public void UnsignedArithmetic()
        => ExitsWith(7, "int main(void) { unsigned int a = 10u; unsigned int b = 3u; return (int)(a - b); }");

    [Test]
    public void FloatingPoint_TruncatesToIntOnReturn()
        => ExitsWith(3, "int main(void) { double x = 3.75; return (int)x; }");

    [Test]
    public void FloatingArithmetic_ComputesThenTruncates()
        => ExitsWith(7, "int main(void) { double a = 2.5; double b = 3.0; return (int)(a * b); }"); // 7.5 -> 7

    [Test]
    public void NestedScopesAndShadowing()
        => ExitsWith(1, "int main(void) { int x = 1; { int x = 2; } return x; }");

    [Test]
    public void CommaOperator()
        => ExitsWith(3, "int main(void) { int a; int b; a = (b = 1, b + 2); return a; }");

    [Test]
    public void SizeofInt_IsFour()
        => ExitsWith(4, "int main(void) { return (int)sizeof(int); }");
}
