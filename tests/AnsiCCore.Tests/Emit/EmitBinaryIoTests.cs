namespace AnsiCCore.Tests.Emit;

/// <summary>End-to-end tests for binary file I/O (fread/fwrite/fseek/ftell/rewind) and the %o/%p/* conversions.</summary>
[TestFixture]
public class EmitBinaryIoTests
{
    private static (int Exit, string Output) RunWithHeaders(string cSource)
        => EmitTestHost.RunCSharpWithOutput(EmitTestHost.TranspileWithDriver(cSource, "p.c", [EmitTestHost.IncludeDir()]));

    private static (int Exit, string Output) RunInline(string cSource)
        => EmitTestHost.RunWithOutput(cSource);

    private static void InTempDirectory(Action action)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ansicc_bin_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string original = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(dir);
        try { action(); }
        finally { Directory.SetCurrentDirectory(original); Directory.Delete(dir, recursive: true); }
    }

    // --- binary I/O -------------------------------------------------------

    [Test]
    public void FwriteThenFread_RoundTripsIntArray()
    {
        const string src = """
            #include <stdio.h>
            int main(void) {
                int out[4]; int i;
                for (i = 0; i < 4; i = i + 1) out[i] = (i + 1) * 100;

                FILE *f = fopen("nums.bin", "w");
                fwrite(out, sizeof(int), 4, f);
                fclose(f);

                int in[4];
                FILE *g = fopen("nums.bin", "r");
                fread(in, sizeof(int), 4, g);
                fclose(g);

                int sum = 0;
                for (i = 0; i < 4; i = i + 1) sum = sum + in[i];
                return sum; /* 100+200+300+400 = 1000 */
            }
            """;
        InTempDirectory(() => Assert.That(RunWithHeaders(src).Exit, Is.EqualTo(1000)));
    }

    [Test]
    public void FseekAndFtell_RandomAccess()
    {
        const string src = """
            #include <stdio.h>
            int main(void) {
                FILE *f = fopen("data.bin", "w");
                fwrite("ABCDEFGH", 1, 8, f);
                fclose(f);

                FILE *g = fopen("data.bin", "r");
                fseek(g, 3, SEEK_SET);
                int c = fgetc(g);            /* 'D' */
                long pos = ftell(g);         /* 4 */
                rewind(g);
                int first = fgetc(g);        /* 'A' */
                fclose(g);
                printf("%c %d %c\n", c, (int)pos, first);
                return 0;
            }
            """;
        InTempDirectory(() => Assert.That(RunWithHeaders(src).Output, Is.EqualTo("D 4 A\n")));
    }

    [Test]
    public void FseekEnd_ReportsFileSize()
    {
        const string src = """
            #include <stdio.h>
            int main(void) {
                FILE *f = fopen("sz.bin", "w");
                fwrite("hello world", 1, 11, f);
                fclose(f);

                FILE *g = fopen("sz.bin", "r");
                fseek(g, 0, SEEK_END);
                long size = ftell(g);
                fclose(g);
                return (int)size; /* 11 */
            }
            """;
        InTempDirectory(() => Assert.That(RunWithHeaders(src).Exit, Is.EqualTo(11)));
    }

    // --- printf %o / %p / * conversions ----------------------------------

    [Test]
    public void Printf_Octal()
        => Assert.That(RunInline("int main(void) { printf(\"%o\\n\", 64); return 0; }").Output, Is.EqualTo("100\n")); // 64 == 0100

    [Test]
    public void Printf_StarWidth()
        => Assert.That(RunInline("int main(void) { printf(\"[%*d]\\n\", 5, 42); return 0; }").Output, Is.EqualTo("[   42]\n"));

    [Test]
    public void Printf_StarPrecision()
        => Assert.That(RunInline("int main(void) { printf(\"[%.*f]\\n\", 2, 3.14159); return 0; }").Output, Is.EqualTo("[3.14]\n"));

    [Test]
    public void Printf_Pointer_IsHex()
    {
        // %p prints the address of an object; we just check the 0x prefix and that two pointers
        // into the same array differ by the expected amount is beyond scope — assert the format.
        const string src = """
            #include <stdio.h>
            int main(void) {
                int x;
                char buf[32];
                sprintf(buf, "%p", (void *)&x);
                printf("%c%c\n", buf[0], buf[1]); /* "0x" */
                return 0;
            }
            """;
        Assert.That(RunWithHeaders(src).Output, Is.EqualTo("0x\n"));
    }
}
