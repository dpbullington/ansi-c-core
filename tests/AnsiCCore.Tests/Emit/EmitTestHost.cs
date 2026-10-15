using System.Reflection;
using AnsiCCore.Emit;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;
using AnsiCCore.Frontend.Parsing;
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

    /// <summary>Transpiles, compiles, and runs the program; returns C <c>main</c>'s exit code.</summary>
    public static int Run(string cSource)
    {
        string csharp = Transpile(cSource);
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

    private static MetadataReference[] BuildReferences()
    {
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var refs = tpa.Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        // Ensure the runtime the emitted code targets is referenced.
        string runtimePath = typeof(CMemory).Assembly.Location;
        if (!tpa.Contains(runtimePath))
            refs.Add(MetadataReference.CreateFromFile(runtimePath));

        return [.. refs];
    }
}
