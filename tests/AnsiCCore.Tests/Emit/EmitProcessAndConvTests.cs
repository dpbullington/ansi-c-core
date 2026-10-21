namespace AnsiCCore.Tests.Emit;

/// <summary>
/// End-to-end tests for the high-value &lt;stdlib.h&gt; tranche: process control (exit/abort/atexit),
/// numeric parsing (strtol/strtoul/strtod/atol/atof), and the rand/srand PRNG.
/// </summary>
[TestFixture]
public class EmitProcessAndConvTests
{
    private static (int Exit, string Output) Run(string cSource)
        => EmitTestHost.RunCSharpWithOutput(EmitTestHost.TranspileWithDriver(cSource, "p.c", [EmitTestHost.IncludeDir()]));

    // --- exit / abort / atexit --------------------------------------------

    [Test]
    public void Exit_TerminatesWithStatus()
    {
        const string src = """
            #include <stdlib.h>
            int deep(void) { exit(7); return 99; }
            int main(void) { deep(); return 1; }
            """;
        Assert.That(Run(src).Exit, Is.EqualTo(7)); // exit unwinds through deep() and main()
    }

    [Test]
    public void Atexit_RunsHandlersLifoOnNormalReturn()
    {
        const string src = """
            #include <stdlib.h>
            #include <stdio.h>
            void a(void) { printf("A"); }
            void b(void) { printf("B"); }
            int main(void) { atexit(a); atexit(b); printf("M"); return 0; }
            """;
        var (exit, output) = Run(src);
        Assert.That(output, Is.EqualTo("MBA")); // main prints M, then handlers run b, a (reverse)
        Assert.That(exit, Is.EqualTo(0));
    }

    [Test]
    public void Atexit_RunsHandlersOnExit()
    {
        const string src = """
            #include <stdlib.h>
            #include <stdio.h>
            void cleanup(void) { printf("C"); }
            int main(void) { atexit(cleanup); printf("M"); exit(3); return 0; }
            """;
        var (exit, output) = Run(src);
        Assert.That(output, Is.EqualTo("MC"));
        Assert.That(exit, Is.EqualTo(3));
    }

    [Test]
    public void Abort_TerminatesAbnormallyWithoutHandlers()
    {
        const string src = """
            #include <stdlib.h>
            #include <stdio.h>
            void handler(void) { printf("H"); }
            int main(void) { atexit(handler); printf("M"); abort(); return 0; }
            """;
        var (exit, output) = Run(src);
        Assert.That(output, Is.EqualTo("M"));   // abort does NOT run atexit handlers
        Assert.That(exit, Is.EqualTo(134));     // 128 + SIGABRT
    }

    // --- strtol / strtoul -------------------------------------------------

    [Test]
    public void Strtol_Base10WithSignAndTrailingText()
        => Assert.That(Run("""
            #include <stdlib.h>
            int main(void) { return (int)strtol("  -42xyz", 0, 10); }
            """).Exit, Is.EqualTo(-42));

    [Test]
    public void Strtol_AutoDetectsHexAndOctal()
    {
        const string src = """
            #include <stdlib.h>
            #include <stdio.h>
            int main(void)
            {
                printf("%ld %ld %ld\n",
                    strtol("0x1A", 0, 0),   /* 26 */
                    strtol("0777", 0, 0),   /* 511 */
                    strtol("100", 0, 0));   /* 100 */
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("26 511 100\n"));
    }

    [Test]
    public void Strtol_SetsEndPointer()
    {
        const string src = """
            #include <stdlib.h>
            #include <stdio.h>
            int main(void)
            {
                char *end;
                long v = strtol("123abc", &end, 10);
                printf("%ld %c\n", v, *end); /* 123 a */
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("123 a\n"));
    }

    [Test]
    public void Strtoul_ParsesLargeUnsigned()
    {
        const string src = """
            #include <stdlib.h>
            #include <stdio.h>
            int main(void) { printf("%lu\n", strtoul("4294967295", 0, 10)); return 0; }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("4294967295\n"));
    }

    // --- strtod / atof / atol ---------------------------------------------

    [Test]
    public void Strtod_ParsesFloatPrefix()
    {
        const string src = """
            #include <stdlib.h>
            #include <stdio.h>
            int main(void)
            {
                printf("%.2f %.1f\n", strtod("3.14xyz", 0), strtod("2.5e2", 0)); /* 3.14 250.0 */
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("3.14 250.0\n"));
    }

    [Test]
    public void AtolAndAtof()
    {
        const string src = """
            #include <stdlib.h>
            #include <stdio.h>
            int main(void) { printf("%ld %.1f\n", atol("  1000zzz"), atof("-0.5")); return 0; }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("1000 -0.5\n"));
    }

    // --- rand / srand -----------------------------------------------------

    [Test]
    public void Rand_IsDeterministicForSeedOne()
    {
        // The classic LCG with seed 1 yields 16838 as its first value.
        const string src = """
            #include <stdlib.h>
            int main(void) { srand(1); return rand(); }
            """;
        Assert.That(Run(src).Exit, Is.EqualTo(16838));
    }

    [Test]
    public void Rand_SameSeedReproducesSequence()
    {
        const string src = """
            #include <stdlib.h>
            int main(void)
            {
                int a;
                int b;
                srand(99); a = rand();
                srand(99); b = rand();
                return a == b ? 1 : 0;
            }
            """;
        Assert.That(Run(src).Exit, Is.EqualTo(1));
    }

    [Test]
    public void Rand_StaysWithinRandMax()
    {
        const string src = """
            #include <stdlib.h>
            int main(void)
            {
                int i;
                int ok = 1;
                for (i = 0; i < 1000; i = i + 1)
                {
                    int r = rand();
                    if (r < 0 || r > RAND_MAX) ok = 0;
                }
                return ok;
            }
            """;
        Assert.That(Run(src).Exit, Is.EqualTo(1));
    }
}
