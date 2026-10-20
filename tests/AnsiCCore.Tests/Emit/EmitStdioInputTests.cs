namespace AnsiCCore.Tests.Emit;

/// <summary>End-to-end tests for stdio input (reads a redirected stdin): scanf, getchar, fgets.</summary>
[TestFixture]
public class EmitStdioInputTests
{
    /// <summary>Runs via the inline path (no preprocessor) with the given stdin, returning exit code and stdout.</summary>
    private static (int Exit, string Output) Run(string cSource, string input)
        => EmitTestHost.RunCSharpWithIO(EmitTestHost.Transpile(cSource), input);

    /// <summary>Runs via the driver (preprocessor + headers) so #include and stdin macros work.</summary>
    private static (int Exit, string Output) RunWithHeaders(string cSource, string input)
        => EmitTestHost.RunCSharpWithIO(EmitTestHost.TranspileWithDriver(cSource, "p.c", [EmitTestHost.IncludeDir()]), input);

    [Test]
    public void Scanf_ReadsInteger()
    {
        const string src = "int main(void) { int x; scanf(\"%d\", &x); return x + 1; }";
        Assert.That(Run(src, "41").Exit, Is.EqualTo(42));
    }

    [Test]
    public void Scanf_ReadsMultipleFields()
    {
        const string src = """
            int main(void) {
                int a; int b;
                scanf("%d %d", &a, &b);
                printf("%d\n", a * b);
                return 0;
            }
            """;
        Assert.That(Run(src, "6 7").Output, Is.EqualTo("42\n"));
    }

    [Test]
    public void Scanf_ReadsString()
    {
        const string src = """
            int main(void) {
                char name[32];
                scanf("%s", name);
                printf("hello %s\n", name);
                return 0;
            }
            """;
        Assert.That(Run(src, "  world rest").Output, Is.EqualTo("hello world\n"));
    }

    [Test]
    public void Getchar_CountsInputUntilEof()
    {
        // getchar returns EOF (-1) at end of input.
        const string src = "int main(void) { int c; int n = 0; while ((c = getchar()) != -1) n = n + 1; return n; }";
        Assert.That(Run(src, "abcde").Exit, Is.EqualTo(5));
    }

    [Test]
    public void Fgets_ReadsALine()
    {
        const string src = """
            #include <stdio.h>
            int main(void) {
                char line[64];
                fgets(line, 64, stdin);
                printf("got: %s", line);
                return 0;
            }
            """;
        Assert.That(RunWithHeaders(src, "first line\nsecond").Output, Is.EqualTo("got: first line\n"));
    }

    [Test]
    public void FprintfToStderr_GoesToErrorStream()
    {
        // fprintf(stderr, ...) writes to Console.Error, not captured stdout.
        const string src = """
            #include <stdio.h>
            int main(void) {
                fprintf(stderr, "error %d\n", 7);
                printf("out\n");
                return 0;
            }
            """;
        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        try
        {
            var (_, output) = RunWithHeaders(src, "");
            Assert.That(output, Is.EqualTo("out\n"));                       // stdout
            Assert.That(capturedError.ToString(), Is.EqualTo("error 7\n")); // stderr
        }
        finally { Console.SetError(originalError); }
    }
}
