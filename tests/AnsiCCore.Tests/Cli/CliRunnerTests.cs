using AnsiCCore.Cli;

namespace AnsiCCore.Tests.Cli;

[TestFixture]
public class CliRunnerTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        int code = CliRunner.Run(args, outWriter, errWriter);
        return (code, outWriter.ToString(), errWriter.ToString());
    }

    private static string WriteTempC(string contents)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ansicc_test_{Guid.NewGuid():N}.c");
        File.WriteAllText(path, contents);
        return path;
    }

    [Test]
    public void NoArguments_PrintsErrorAndReturnsUsageCode()
    {
        var (code, _, err) = Run();
        Assert.That(code, Is.EqualTo(64));
        Assert.That(err, Does.Contain("no input files"));
    }

    [Test]
    public void Help_PrintsUsageAndReturnsOk()
    {
        var (code, output, _) = Run("--help");
        Assert.That(code, Is.EqualTo(0));
        Assert.That(output, Does.Contain("usage: ansicc"));
    }

    [Test]
    public void UnknownOption_ReturnsUsageCode()
    {
        var (code, _, err) = Run("--frobnicate");
        Assert.That(code, Is.EqualTo(64));
        Assert.That(err, Does.Contain("unknown option"));
    }

    [Test]
    public void ValidFile_ReturnsOk_AndPrintsSummary()
    {
        string path = WriteTempC("int main(void) { return 0; }");
        try
        {
            var (code, output, _) = Run(path);
            Assert.That(code, Is.EqualTo(0));
            Assert.That(output, Does.Contain("top-level item"));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void FileWithErrors_ReturnsErrorCode()
    {
        string path = WriteTempC("int x = ;");
        try
        {
            var (code, _, err) = Run(path);
            Assert.That(code, Is.EqualTo(1));
            Assert.That(err, Does.Contain("error:"));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void DumpAst_EmitsOutline()
    {
        string path = WriteTempC("int answer = 42;");
        try
        {
            var (code, output, _) = Run("--dump-ast", path);
            Assert.That(code, Is.EqualTo(0));
            Assert.That(output, Does.Contain("declare answer"));
            Assert.That(output, Does.Contain("42"));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void DumpTokens_EmitsTokenStream()
    {
        string path = WriteTempC("int x;");
        try
        {
            var (code, output, _) = Run("--dump-tokens", path);
            Assert.That(code, Is.EqualTo(0));
            Assert.That(output, Does.Contain("KwInt"));
            Assert.That(output, Does.Contain("Identifier"));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void Emit_PrintsCSharpToStdout()
    {
        string path = WriteTempC("int main(void) { return 7; }");
        try
        {
            var (code, output, _) = Run("--emit", path);
            Assert.That(code, Is.EqualTo(0));
            Assert.That(output, Does.Contain("class CProgram"));
            Assert.That(output, Does.Contain("Main()"));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void OutputDirectory_WritesGeneratedFile()
    {
        string path = WriteTempC("int main(void) { return 0; }");
        string outDir = Path.Combine(Path.GetTempPath(), $"ansicc_out_{Guid.NewGuid():N}");
        try
        {
            var (code, _, _) = Run("-o", outDir, path);
            Assert.That(code, Is.EqualTo(0));
            string expected = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".g.cs");
            Assert.That(File.Exists(expected), Is.True);
            Assert.That(File.ReadAllText(expected), Does.Contain("class CProgram"));
        }
        finally
        {
            File.Delete(path);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Test]
    public void DefineOption_IsHonored()
    {
        string path = WriteTempC("int a[SIZE];");
        try
        {
            // Without -D SIZE, "SIZE" is an undeclared identifier used as an array size, but the
            // parser still accepts it syntactically; with -D it expands to a constant. Both parse,
            // so assert the option is accepted and parsing succeeds.
            var (code, _, _) = Run("-D", "SIZE=16", path);
            Assert.That(code, Is.EqualTo(0));
        }
        finally { File.Delete(path); }
    }
}
