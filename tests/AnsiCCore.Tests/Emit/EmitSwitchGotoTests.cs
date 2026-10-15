namespace AnsiCCore.Tests.Emit;

/// <summary>End-to-end tests for switch (with fall-through) and goto/labels.</summary>
[TestFixture]
public class EmitSwitchGotoTests
{
    private static void ExitsWith(int expected, string cSource)
        => Assert.That(EmitTestHost.Run(cSource), Is.EqualTo(expected), () => EmitTestHost.Transpile(cSource));

    [Test]
    public void Switch_SelectsCase()
    {
        const string src = """
            int main(void) {
                int x = 2;
                switch (x) {
                    case 1: return 10;
                    case 2: return 20;
                    case 3: return 30;
                    default: return 0;
                }
            }
            """;
        ExitsWith(20, src);
    }

    [Test]
    public void Switch_DefaultWhenNoMatch()
    {
        const string src = """
            int main(void) {
                int x = 99;
                switch (x) {
                    case 1: return 1;
                    default: return 42;
                }
            }
            """;
        ExitsWith(42, src);
    }

    [Test]
    public void Switch_NoMatchNoDefault_FallsOut()
    {
        const string src = """
            int main(void) {
                int r = 7;
                switch (3) { case 1: r = 1; break; case 2: r = 2; break; }
                return r;
            }
            """;
        ExitsWith(7, src);
    }

    [Test]
    public void Switch_FallThrough_WithoutBreak()
    {
        // No breaks: matching case 1 falls through, accumulating all three.
        const string src = """
            int main(void) {
                int x = 1;
                int sum = 0;
                switch (x) {
                    case 1: sum = sum + 1;
                    case 2: sum = sum + 10;
                    case 3: sum = sum + 100;
                }
                return sum;
            }
            """;
        ExitsWith(111, src);
    }

    [Test]
    public void Switch_BreakStopsFallThrough()
    {
        const string src = """
            int main(void) {
                int x = 2;
                int sum = 0;
                switch (x) {
                    case 1: sum = sum + 1;
                    case 2: sum = sum + 10; break;
                    case 3: sum = sum + 100;
                }
                return sum;
            }
            """;
        ExitsWith(10, src);
    }

    [Test]
    public void Switch_SharedCaseLabels()
    {
        // Consecutive labels (case 1: case 2:) share the same body.
        const string src = """
            int classify(int x) {
                switch (x) {
                    case 1:
                    case 2:
                    case 3: return 1;
                    default: return 0;
                }
            }
            int main(void) { return classify(2) + classify(5) * 10; }
            """;
        ExitsWith(1, src); // classify(2)=1, classify(5)=0
    }

    [Test]
    public void Switch_CharExpression()
    {
        const string src = """
            int main(void) {
                char c = 'b';
                switch (c) {
                    case 'a': return 1;
                    case 'b': return 2;
                    default: return 0;
                }
            }
            """;
        ExitsWith(2, src);
    }

    [Test]
    public void Switch_BreakInLoopInsideSwitch_TargetsLoop()
    {
        const string src = """
            int main(void) {
                int total = 0;
                switch (1) {
                    case 1: {
                        int i;
                        for (i = 0; i < 10; i = i + 1) { if (i == 3) break; total = total + i; }
                    }
                }
                return total; /* 0+1+2 = 3 */
            }
            """;
        ExitsWith(3, src);
    }

    [Test]
    public void Goto_ForwardJump()
    {
        const string src = """
            int main(void) {
                int x = 0;
                goto skip;
                x = 100;
            skip:
                return x;
            }
            """;
        ExitsWith(0, src);
    }

    [Test]
    public void Goto_BackwardLoop()
    {
        const string src = """
            int main(void) {
                int i = 0;
                int sum = 0;
            again:
                sum = sum + i;
                i = i + 1;
                if (i <= 5) goto again;
                return sum; /* 0+1+2+3+4+5 = 15 */
            }
            """;
        ExitsWith(15, src);
    }

    [Test]
    public void Goto_BreaksOutOfNestedLoops()
    {
        const string src = """
            int main(void) {
                int i; int j; int found = 0;
                for (i = 0; i < 5; i = i + 1)
                    for (j = 0; j < 5; j = j + 1)
                        if (i * 5 + j == 13) { found = i * 10 + j; goto done; }
            done:
                return found; /* i=2,j=3 -> 23 */
            }
            """;
        ExitsWith(23, src);
    }
}
