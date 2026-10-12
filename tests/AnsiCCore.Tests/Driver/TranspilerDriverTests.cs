using AnsiCCore.Driver;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Preprocessing;

namespace AnsiCCore.Tests.Driver;

[TestFixture]
public class TranspilerDriverTests
{
    private static TranslationUnitResult RunSource(string src, DriverOptions? options = null, IIncludeResolver? resolver = null)
        => new TranspilerDriver(options).RunSource(src, "main.c", resolver ?? new InMemoryIncludeResolver(new Dictionary<string, string>()));

    [Test]
    public void ValidProgram_ParsesWithoutErrors()
    {
        var result = RunSource("int main(void) { return 0; }");
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.False);
            Assert.That(result.Ast, Is.Not.Null);
            Assert.That(result.Ast!.Items, Has.Count.EqualTo(1));
            Assert.That(result.Tokens, Is.Not.Empty);
        });
    }

    [Test]
    public void Preprocessing_And_Parsing_AreWiredTogether()
    {
        // Macro must expand before the parser sees the array size.
        var result = RunSource("#define N 3\nint a[N];");
        Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.False);
        Assert.That(result.Ast!.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public void Include_IsResolvedThroughResolver()
    {
        var resolver = new InMemoryIncludeResolver(new Dictionary<string, string>
        {
            ["defs.h"] = "typedef int myint;",
        });
        var result = RunSource("#include \"defs.h\"\nmyint value;", resolver: resolver);
        Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.False);
        // Two items: the typedef (from the header) and the variable declaration.
        Assert.That(result.Ast!.Items, Has.Count.EqualTo(2));
    }

    [Test]
    public void PredefinedMacro_FlowsFromOptions()
    {
        var options = new DriverOptions
        {
            PredefinedMacros = new Dictionary<string, string?> { ["WIDTH"] = "8" },
        };
        var result = RunSource("int bits[WIDTH];", options);
        Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.False);
    }

    [Test]
    public void SyntaxError_IsSurfaced()
    {
        var result = RunSource("int x = ;");
        Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.True);
    }

    [Test]
    public void PreprocessorError_IsSurfaced()
    {
        var result = RunSource("#error boom");
        Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.True);
    }

    [Test]
    public void Run_MissingFile_ReportsErrorWithoutThrowing()
    {
        var result = new TranspilerDriver().Run(["/no/such/path/definitely-missing.c"]);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Units, Has.Count.EqualTo(1));
    }
}
