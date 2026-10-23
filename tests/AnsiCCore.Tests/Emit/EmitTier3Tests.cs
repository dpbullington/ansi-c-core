namespace AnsiCCore.Tests.Emit;

/// <summary>
/// End-to-end tests for Tier 3: &lt;assert.h&gt; (with NDEBUG and call-site reporting),
/// &lt;signal.h&gt; (signal/raise/SIG_IGN/SIG_DFL), and &lt;setjmp.h&gt; (setjmp/longjmp across both
/// the if-condition and assignment idioms, with correct break targeting).
/// </summary>
[TestFixture]
public class EmitTier3Tests
{
    private static (int Exit, string Output) Run(string cSource)
        => EmitTestHost.RunCSharpWithOutput(EmitTestHost.TranspileWithDriver(cSource, "p.c", [EmitTestHost.IncludeDir()]));

    // --- assert -----------------------------------------------------------

    [Test]
    public void Assert_PassingIsSilent()
    {
        const string src = """
            #include <assert.h>
            #include <stdio.h>
            int main(void) { int x = 5; assert(x == 5); printf("ok\n"); return 0; }
            """;
        var (exit, output) = Run(src);
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(output, Is.EqualTo("ok\n"));
    }

    [Test]
    public void Assert_FailingAborts()
    {
        const string src = """
            #include <assert.h>
            int main(void) { int x = 5; assert(x == 6); return 0; }
            """;
        Assert.That(Run(src).Exit, Is.EqualTo(134)); // abort
    }

    [Test]
    public void Assert_NdebugDisables()
    {
        const string src = """
            #define NDEBUG
            #include <assert.h>
            #include <stdio.h>
            int main(void) { assert(0); printf("reached\n"); return 0; }
            """;
        var (exit, output) = Run(src);
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(output, Is.EqualTo("reached\n"));
    }

    // --- signal -----------------------------------------------------------

    [Test]
    public void Signal_HandlerRunsOnRaiseThenIgnored()
    {
        const string src = """
            #include <signal.h>
            #include <stdio.h>
            static int hits = 0;
            void onsig(int s) { hits = hits + s; }
            int main(void)
            {
                signal(SIGINT, onsig);
                raise(SIGINT);           /* onsig(2) */
                signal(SIGINT, SIG_IGN);
                raise(SIGINT);           /* ignored */
                signal(SIGINT, SIG_DFL);
                printf("hits=%d\n", hits);
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("hits=2\n"));
    }

    // --- setjmp / longjmp -------------------------------------------------

    [Test]
    public void Setjmp_AssignmentIdiom()
    {
        const string src = """
            #include <setjmp.h>
            #include <stdio.h>
            static jmp_buf env;
            void deep(int n) { longjmp(env, n * 10); }
            int main(void)
            {
                int r = setjmp(env);
                if (r == 0) { printf("first\n"); deep(4); printf("unreachable\n"); }
                else        { printf("back r=%d\n", r); }
                return r;
            }
            """;
        var (exit, output) = Run(src);
        Assert.That(output, Is.EqualTo("first\nback r=40\n"));
        Assert.That(exit, Is.EqualTo(40));
    }

    [Test]
    public void Setjmp_IfConditionIdiom()
    {
        const string src = """
            #include <setjmp.h>
            #include <stdio.h>
            int main(void)
            {
                jmp_buf e;
                if (setjmp(e)) { printf("handler\n"); return 2; }
                printf("work\n");
                longjmp(e, 1);
                return 0;
            }
            """;
        var (exit, output) = Run(src);
        Assert.That(output, Is.EqualTo("work\nhandler\n"));
        Assert.That(exit, Is.EqualTo(2));
    }

    [Test]
    public void Setjmp_BreakInsideGuardedRegionTargetsLoop()
    {
        // The retry region is goto-based, so this `break` must target the for loop, not the guard.
        const string src = """
            #include <setjmp.h>
            #include <stdio.h>
            int main(void)
            {
                jmp_buf e;
                int i;
                int found = -1;
                int r = setjmp(e);
                if (r == 0)
                {
                    for (i = 0; i < 10; i = i + 1)
                        if (i == 4) { found = i; break; }
                    longjmp(e, found + 1);
                }
                printf("r=%d\n", r);
                return r;
            }
            """;
        var (exit, output) = Run(src);
        Assert.That(output, Is.EqualTo("r=5\n"));
        Assert.That(exit, Is.EqualTo(5));
    }

    [Test]
    public void Setjmp_LongjmpValueThreadsThrough()
    {
        // longjmp(env, 0) must make setjmp appear to return 1.
        const string src = """
            #include <setjmp.h>
            #include <stdio.h>
            int main(void)
            {
                jmp_buf e;
                int r = setjmp(e);
                if (r == 0) longjmp(e, 0); /* 0 is remapped to 1 */
                printf("r=%d\n", r);
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("r=1\n"));
    }

    [Test]
    public void Setjmp_NestedInExpression()
    {
        // setjmp need not be the whole controlling expression; it is found anywhere in the item.
        const string src = """
            #include <setjmp.h>
            #include <stdio.h>
            static jmp_buf e;
            int main(void)
            {
                int r = 5 + setjmp(e); /* setjmp nested in a binary expression */
                if (r == 5) longjmp(e, 3);
                printf("r=%d\n", r); /* 5 + 3 = 8 */
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("r=8\n"));
    }

    [Test]
    public void Setjmp_InReturnExpression()
    {
        // setjmp in a return expression is accepted (first and only pass returns its direct value).
        const string src = """
            #include <setjmp.h>
            int main(void) { jmp_buf e; return setjmp(e) + 7; }
            """;
        Assert.That(Run(src).Exit, Is.EqualTo(7));
    }
}
