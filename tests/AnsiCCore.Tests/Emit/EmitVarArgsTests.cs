namespace AnsiCCore.Tests.Emit;

/// <summary>
/// End-to-end tests for &lt;stdarg.h&gt; in user-defined variadic functions: va_start/va_arg/va_end
/// over the hidden promoted-argument region built at each call site.
/// </summary>
[TestFixture]
public class EmitVarArgsTests
{
    private static (int Exit, string Output) Run(string cSource)
        => EmitTestHost.RunCSharpWithOutput(EmitTestHost.TranspileWithDriver(cSource, "p.c", [EmitTestHost.IncludeDir()]));

    [Test]
    public void SumsIntArguments()
    {
        const string src = """
            #include <stdarg.h>
            int sum(int count, ...)
            {
                int total = 0;
                int i;
                va_list ap;
                va_start(ap, count);
                for (i = 0; i < count; i = i + 1)
                    total = total + va_arg(ap, int);
                va_end(ap);
                return total;
            }
            int main(void) { return sum(4, 10, 20, 30, 40); } /* 100 */
            """;
        Assert.That(Run(src).Exit, Is.EqualTo(100));
    }

    [Test]
    public void ZeroVariadicArguments()
    {
        const string src = """
            #include <stdarg.h>
            int sum(int count, ...)
            {
                int total = 0;
                int i;
                va_list ap;
                va_start(ap, count);
                for (i = 0; i < count; i = i + 1)
                    total = total + va_arg(ap, int);
                va_end(ap);
                return total;
            }
            int main(void) { return sum(0); } /* no variadic args -> 0 */
            """;
        Assert.That(Run(src).Exit, Is.EqualTo(0));
    }

    [Test]
    public void MixedIntAndDoubleArguments()
    {
        // Promotions: the int passes in a widened slot, the double in its own 8-byte slot.
        const string src = """
            #include <stdarg.h>
            #include <stdio.h>
            double weighted(int n, ...)
            {
                double acc = 0.0;
                int i;
                va_list ap;
                va_start(ap, n);
                for (i = 0; i < n; i = i + 1)
                {
                    int w = va_arg(ap, int);
                    double v = va_arg(ap, double);
                    acc = acc + w * v;
                }
                va_end(ap);
                return acc;
            }
            int main(void)
            {
                printf("%.1f\n", weighted(2, 3, 1.5, 2, 4.0)); /* 3*1.5 + 2*4.0 = 12.5 */
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("12.5\n"));
    }

    [Test]
    public void PointerArgumentsAndCounting()
    {
        // char* arguments flow through the va_list as pointers; concatenate their lengths.
        const string src = """
            #include <stdarg.h>
            #include <string.h>
            int total_len(int n, ...)
            {
                int total = 0;
                int i;
                va_list ap;
                va_start(ap, n);
                for (i = 0; i < n; i = i + 1)
                    total = total + (int)strlen(va_arg(ap, char *));
                va_end(ap);
                return total;
            }
            int main(void) { return total_len(3, "ab", "cde", "f"); } /* 2 + 3 + 1 = 6 */
            """;
        Assert.That(Run(src).Exit, Is.EqualTo(6));
    }

    [Test]
    public void ForwardsToVprintfStyleUsage()
    {
        // A user variadic function that itself formats each argument and prints a running total.
        const string src = """
            #include <stdarg.h>
            #include <stdio.h>
            void print_all(int n, ...)
            {
                int i;
                va_list ap;
                va_start(ap, n);
                for (i = 0; i < n; i = i + 1)
                    printf("[%d]", va_arg(ap, int));
                va_end(ap);
                printf("\n");
            }
            int main(void) { print_all(3, 7, 8, 9); return 0; }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("[7][8][9]\n"));
    }

    [Test]
    public void SecondTraversalRestartsFromStart()
    {
        // Two independent va_start/va_end passes over the same call's arguments.
        const string src = """
            #include <stdarg.h>
            int twice_sum(int n, ...)
            {
                int total = 0;
                int pass;
                int i;
                for (pass = 0; pass < 2; pass = pass + 1)
                {
                    va_list ap;
                    va_start(ap, n);
                    for (i = 0; i < n; i = i + 1)
                        total = total + va_arg(ap, int);
                    va_end(ap);
                }
                return total;
            }
            int main(void) { return twice_sum(3, 1, 2, 3); } /* (1+2+3) * 2 = 12 */
            """;
        Assert.That(Run(src).Exit, Is.EqualTo(12));
    }
}
