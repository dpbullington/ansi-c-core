namespace AnsiCCore.Tests.Emit;

/// <summary>
/// End-to-end tests for two C89 frontend/emit features: old-style (K&amp;R) function definitions
/// and trigraph translation (translation phase 1).
/// </summary>
[TestFixture]
public class EmitKnrAndTrigraphTests
{
    private static (int Exit, string Output) RunWithDriver(string cSource)
        => EmitTestHost.RunCSharpWithOutput(EmitTestHost.TranspileWithDriver(cSource, "p.c", [EmitTestHost.IncludeDir()]));

    // --- K&R function definitions -----------------------------------------

    [Test]
    public void Knr_BasicTwoParameters()
    {
        const string src = """
            int add(a, b)
            int a, b;
            { return a + b; }
            int main(void) { return add(19, 23); }
            """;
        Assert.That(EmitTestHost.Run(src), Is.EqualTo(42));
    }

    [Test]
    public void Knr_UndeclaredParameterDefaultsToInt()
    {
        const string src = """
            int pick(a, b)
            int b; /* a has no declaration -> defaults to int */
            { return a + b; }
            int main(void) { return pick(40, 2); }
            """;
        Assert.That(EmitTestHost.Run(src), Is.EqualTo(42));
    }

    [Test]
    public void Knr_PointerParameterSumsArray()
    {
        const string src = """
            int total(p, n)
            int *p;
            int n;
            {
                int i, sum = 0;
                for (i = 0; i < n; i = i + 1) sum = sum + p[i];
                return sum;
            }
            int main(void) { int v[4]; v[0]=1; v[1]=2; v[2]=3; v[3]=4; return total(v, 4); }
            """;
        Assert.That(EmitTestHost.Run(src), Is.EqualTo(10));
    }

    [Test]
    public void Knr_AddressTakenParameterIsSpilled()
    {
        const string src = """
            int bump(x)
            int x;
            {
                int *p = &x; /* taking &x forces the K&R parameter into memory */
                *p = *p + 100;
                return x;
            }
            int main(void) { return bump(5); }
            """;
        Assert.That(EmitTestHost.Run(src), Is.EqualTo(105));
    }

    // --- trigraphs --------------------------------------------------------

    [Test]
    public void Trigraph_BracesAndBrackets()
    {
        // ??< ??> are { }, and this exercises translation before tokenization.
        const string src = "int main(void) ??< return 42; ??>";
        Assert.That(EmitTestHost.Run(src), Is.EqualTo(42));
    }

    [Test]
    public void Trigraph_InsideStringLiteral()
    {
        // Trigraphs are replaced everywhere, including in string literals: ??( ??) -> [ ] and ??/n -> \n.
        const string src = """
            #include <stdio.h>
            int main(void) { printf("a??(b??)c??/n"); return 0; }
            """;
        Assert.That(RunWithDriver(src).Output, Is.EqualTo("a[b]c\n"));
    }

    [Test]
    public void Trigraph_HashIncludeAndFullProgram()
    {
        // ??= is #, so the include directive itself is written with a trigraph.
        const string src = """
            ??=include <stdio.h>
            int main(void) ??<
                int x = 7;
                printf("x=%d??/n", x);
                return 0;
            ??>
            """;
        Assert.That(RunWithDriver(src).Output, Is.EqualTo("x=7\n"));
    }
}
