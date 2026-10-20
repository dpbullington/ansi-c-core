namespace AnsiCCore.Tests.Emit;

/// <summary>
/// End-to-end tests for the rounded-out scanf conversions (%x, %f, width) and for file streams
/// (fopen/fclose/fputs/fprintf/fgets/fscanf) backed by the real file system.
/// </summary>
[TestFixture]
public class EmitFileStreamTests
{
    private static (int Exit, string Output) RunInline(string cSource, string input)
        => EmitTestHost.RunCSharpWithIO(EmitTestHost.Transpile(cSource), input);

    private static (int Exit, string Output) RunWithHeaders(string cSource)
        => EmitTestHost.RunCSharpWithOutput(EmitTestHost.TranspileWithDriver(cSource, "p.c", [EmitTestHost.IncludeDir()]));

    // --- scanf conversions ------------------------------------------------

    [Test]
    public void Scanf_Hex()
    {
        const string src = "int main(void) { int x; scanf(\"%x\", &x); return x; }";
        Assert.That(RunInline(src, "ff").Exit, Is.EqualTo(255));
    }

    [Test]
    public void Scanf_Float()
    {
        const string src = """
            int main(void) { float f; scanf("%f", &f); printf("%.2f\n", f); return 0; }
            """;
        Assert.That(RunInline(src, "3.5").Output, Is.EqualTo("3.50\n"));
    }

    [Test]
    public void Scanf_Double_WithLengthModifier()
    {
        const string src = """
            int main(void) { double d; scanf("%lf", &d); printf("%.3f\n", d); return 0; }
            """;
        Assert.That(RunInline(src, "2.25").Output, Is.EqualTo("2.250\n"));
    }

    [Test]
    public void Scanf_FieldWidthLimitsReading()
    {
        // %2d consumes only the first two characters.
        const string src = "int main(void) { int x; scanf(\"%2d\", &x); return x; }";
        Assert.That(RunInline(src, "123").Exit, Is.EqualTo(12));
    }

    // --- file streams -----------------------------------------------------

    [Test]
    public void FileRoundTrip_WriteThenRead()
    {
        const string src = """
            #include <stdio.h>
            int main(void) {
                FILE *f = fopen("rt.txt", "w");
                fputs("line one\n", f);
                fprintf(f, "num=%d\n", 42);
                fclose(f);

                FILE *g = fopen("rt.txt", "r");
                char buf[64];
                while (fgets(buf, 64, g) != 0) printf("%s", buf);
                fclose(g);
                return 0;
            }
            """;
        InTempDirectory(() => Assert.That(RunWithHeaders(src).Output, Is.EqualTo("line one\nnum=42\n")));
    }

    [Test]
    public void FileScanf_ReadsValues()
    {
        const string src = """
            #include <stdio.h>
            int main(void) {
                FILE *f = fopen("data.txt", "w");
                fprintf(f, "7 8 hello");
                fclose(f);

                FILE *g = fopen("data.txt", "r");
                int a; int b; char word[16];
                fscanf(g, "%d %d %s", &a, &b, word);
                fclose(g);
                printf("%d %s\n", a + b, word);
                return 0;
            }
            """;
        InTempDirectory(() => Assert.That(RunWithHeaders(src).Output, Is.EqualTo("15 hello\n")));
    }

    [Test]
    public void FopenMissingFile_ReturnsNull()
    {
        const string src = """
            #include <stdio.h>
            int main(void) {
                FILE *f = fopen("does-not-exist.txt", "r");
                return f == 0 ? 1 : 0;
            }
            """;
        InTempDirectory(() => Assert.That(RunWithHeaders(src).Output, Is.Empty));
        InTempDirectory(() => Assert.That(EmitTestHost.RunCSharpWithOutput(
            EmitTestHost.TranspileWithDriver(src, "p.c", [EmitTestHost.IncludeDir()])).Exit, Is.EqualTo(1)));
    }

    /// <summary>Runs <paramref name="action"/> with the working directory set to a fresh temp directory.</summary>
    private static void InTempDirectory(Action action)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ansicc_fs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string original = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(dir);
        try { action(); }
        finally
        {
            Directory.SetCurrentDirectory(original);
            Directory.Delete(dir, recursive: true);
        }
    }
}
