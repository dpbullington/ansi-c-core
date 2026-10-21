namespace AnsiCCore.Tests.Emit;

/// <summary>End-to-end tests for &lt;math.h&gt; and &lt;time.h&gt;.</summary>
[TestFixture]
public class EmitMathTimeTests
{
    private static (int Exit, string Output) Run(string cSource)
        => EmitTestHost.RunCSharpWithOutput(EmitTestHost.TranspileWithDriver(cSource, "p.c", [EmitTestHost.IncludeDir()]));

    // --- math.h -----------------------------------------------------------

    [Test]
    public void Math_IntArgumentsConvertToDouble()
    {
        // pow's prototype is double(double,double); int literals convert at the call.
        const string src = """
            #include <math.h>
            int main(void) { return (int)pow(2, 10); }
            """;
        Assert.That(Run(src).Exit, Is.EqualTo(1024));
    }

    [Test]
    public void Math_SqrtAndTrig()
    {
        const string src = """
            #include <math.h>
            #include <stdio.h>
            int main(void) {
                printf("%.3f %.3f %.3f\n", sqrt(9.0), sin(0.0), cos(0.0));
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("3.000 0.000 1.000\n"));
    }

    [Test]
    public void Math_RoundingFamily()
    {
        const string src = """
            #include <math.h>
            int main(void) {
                return (int)(floor(2.9) + ceil(2.1) + round(2.5) + trunc(2.9)); /* 2 + 3 + 3 + 2 = 10 */
            }
            """;
        Assert.That(Run(src).Exit, Is.EqualTo(10));
    }

    [Test]
    public void Math_Constant_M_PI()
    {
        const string src = """
            #include <math.h>
            #include <stdio.h>
            int main(void) { printf("%.5f\n", M_PI); return 0; }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("3.14159\n"));
    }

    // --- time.h -----------------------------------------------------------

    [Test]
    public void Time_ReturnsPositiveEpochSeconds()
    {
        const string src = """
            #include <time.h>
            int main(void) { time_t t = time(0); return t > 0 ? 1 : 0; }
            """;
        Assert.That(Run(src).Exit, Is.EqualTo(1));
    }

    [Test]
    public void Time_WritesThroughPointer()
    {
        // time(&x) returns the same value it stores through the pointer.
        const string src = """
            #include <time.h>
            int main(void) { time_t x; time_t r = time(&x); return r == x ? 1 : 0; }
            """;
        Assert.That(Run(src).Exit, Is.EqualTo(1));
    }

    [Test]
    public void Difftime_ComputesDifference()
    {
        const string src = """
            #include <time.h>
            int main(void) { return (int)difftime(100, 40); }
            """;
        Assert.That(Run(src).Exit, Is.EqualTo(60));
    }
}
