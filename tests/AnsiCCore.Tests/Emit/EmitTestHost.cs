using System.Reflection;
using AnsiCCore.Driver;
using AnsiCCore.Emit;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;
using AnsiCCore.Frontend.Parsing;
using AnsiCCore.Runtime.Library;
using AnsiCCore.Runtime.Memory;
using AnsiCCore.Semantics.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using CSeverity = AnsiCCore.Frontend.Diagnostics.DiagnosticSeverity;
using RoslynSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace AnsiCCore.Tests.Emit;

/// <summary>
/// End-to-end harness for Emit tests: runs C source through the full pipeline
/// (lex → parse → analyze → emit), compiles the emitted C# in memory with Roslyn, runs its
/// entry point, and returns the process exit code (which is C <c>main</c>'s return value).
/// </summary>
internal static class EmitTestHost
{
    private static readonly MetadataReference[] References = BuildReferences();

    /// <summary>Transpiles C source to C#, asserting there were no semantic errors.</summary>
    public static string Transpile(string cSource)
    {
        var tokens = new Lexer(cSource, "t.c").Tokenize();
        var bag = new DiagnosticBag();
        var unit = new Parser(tokens, bag).ParseTranslationUnit();
        var model = SemanticAnalyzer.Analyze(unit, bag);

        var errors = bag.Where(d => d.Severity == CSeverity.Error).ToList();
        Assert.That(errors, Is.Empty, () => "C diagnostics: " + string.Join("; ", errors));

        return CSharpEmitter.Emit(unit, model);
    }

    /// <summary>
    /// Transpiles with <paramref name="linked"/> assemblies added to the import library, so a C
    /// extern call resolves to a <c>[CExport]</c> method in them. Demonstrates link-time binding.
    /// </summary>
    public static string TranspileLinking(string cSource, params Assembly[] linked)
    {
        var tokens = new Lexer(cSource, "t.c").Tokenize();
        var bag = new DiagnosticBag();
        var unit = new Parser(tokens, bag).ParseTranslationUnit();
        var model = SemanticAnalyzer.Analyze(unit, bag);

        var errors = bag.Where(d => d.Severity == CSeverity.Error).ToList();
        Assert.That(errors, Is.Empty, () => "C diagnostics: " + string.Join("; ", errors));

        var registry = ExportRegistry.FromAssemblies(linked.Prepend(typeof(CStdLib).Assembly));
        return CSharpEmitter.Emit(unit, model, registry);
    }

    public static int RunLinking(string cSource, params Assembly[] linked) => RunCSharp(TranspileLinking(cSource, linked));

    /// <summary>
    /// Transpiles through the full driver (preprocessor + include resolution + analysis + emit),
    /// so programs may use <c>#include</c> and macros. Asserts there were no errors.
    /// </summary>
    public static string TranspileWithDriver(string cSource, string path, IEnumerable<string> includeDirs)
    {
        var options = new DriverOptions { IncludeDirectories = [.. includeDirs], EmitCSharp = true };
        var result = new TranspilerDriver(options).RunSource(cSource, path);

        var errors = result.Diagnostics.Where(d => d.Severity == CSeverity.Error).ToList();
        Assert.That(errors, Is.Empty, () => "C diagnostics: " + string.Join("; ", errors));
        return result.EmittedCSharp ?? throw new AssertionException("driver produced no C#");
    }

    /// <summary>Compiles already-emitted C# and runs its entry point, returning the exit code.</summary>
    public static int RunCSharp(string csharp)
    {
        var tree = CSharpSyntaxTree.ParseText(csharp);
        var compilation = CSharpCompilation.Create(
            "Emitted",
            [tree],
            References,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Release));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            var errors = result.Diagnostics.Where(d => d.Severity == RoslynSeverity.Error);
            Assert.Fail("emitted C# failed to compile:\n" + string.Join("\n", errors) + "\n\n--- source ---\n" + csharp);
        }

        stream.Position = 0;
        var assembly = Assembly.Load(stream.ToArray());
        var entry = assembly.EntryPoint ?? throw new AssertionException("emitted assembly has no entry point");
        object? value = entry.Invoke(null, entry.GetParameters().Length == 0 ? null : [Array.Empty<string>()]);
        return value is int code ? code : 0;
    }

    /// <summary>Transpiles, compiles, and runs the program; returns C <c>main</c>'s exit code.</summary>
    public static int Run(string cSource) => RunCSharp(Transpile(cSource));

    /// <summary>Compiles and runs already-emitted C#, capturing both exit code and stdout.</summary>
    public static (int Exit, string Output) RunCSharpWithOutput(string csharp)
    {
        var original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            return (RunCSharp(csharp), captured.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    /// <summary>Runs the program (inline path) and returns both its exit code and stdout.</summary>
    public static (int Exit, string Output) RunWithOutput(string cSource) => RunCSharpWithOutput(Transpile(cSource));

    private static MetadataReference[] BuildReferences()
    {
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var refs = tpa.Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        // Reference the runtime the emitted code targets, plus this test assembly so programs can
        // link against [CExport] methods defined in the tests.
        foreach (var path in new[] { typeof(CMemory).Assembly.Location, typeof(EmitTestHost).Assembly.Location })
            if (!tpa.Contains(path))
                refs.Add(MetadataReference.CreateFromFile(path));

        return [.. refs];
    }
}
