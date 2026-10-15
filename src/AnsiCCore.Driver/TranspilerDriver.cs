using AnsiCCore.Emit;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;
using AnsiCCore.Frontend.Parsing;
using AnsiCCore.Frontend.Preprocessing;
using AnsiCCore.Semantics.Analysis;

namespace AnsiCCore.Driver;

/// <summary>
/// Orchestrates the compiler pipeline across a set of translation units. Today it runs the
/// Frontend (preprocess → parse); Semantics and Emit will slot in behind the parse step as
/// they come online. Owns multi-file / whole-program concerns and diagnostic aggregation.
/// </summary>
public sealed class TranspilerDriver
{
    private readonly DriverOptions _options;

    public TranspilerDriver(DriverOptions? options = null) => _options = options ?? DriverOptions.Default;

    /// <summary>Reads and processes each input path from disk.</summary>
    public DriverResult Run(IEnumerable<string> inputPaths)
    {
        var resolver = new FileSystemIncludeResolver(_options.IncludeDirectories, _options.IncludeDirectories);
        var units = new List<TranslationUnitResult>();

        foreach (var path in inputPaths)
        {
            string? source = TryReadFile(path, out var readError);
            if (source == null)
            {
                var bag = new DiagnosticBag();
                bag.Error(new SourceLocation(path, 0, 0, 0), readError ?? "could not read file");
                units.Add(new TranslationUnitResult(path, [], null, null, null, bag.ToList()));
                continue;
            }

            units.Add(ProcessSource(source, path, resolver));
        }

        return new DriverResult(units);
    }

    /// <summary>
    /// Processes a single in-memory translation unit. Useful for tests and for callers that
    /// already hold source text; <paramref name="includeResolver"/> defaults to the configured
    /// file-system resolver.
    /// </summary>
    public TranslationUnitResult RunSource(string source, string path, IIncludeResolver? includeResolver = null)
    {
        var resolver = includeResolver ?? new FileSystemIncludeResolver(_options.IncludeDirectories, _options.IncludeDirectories);
        return ProcessSource(source, path, resolver);
    }

    private TranslationUnitResult ProcessSource(string source, string path, IIncludeResolver resolver)
    {
        // One diagnostic bag threaded through every Frontend stage so messages stay ordered.
        var diagnostics = new DiagnosticBag();

        var ppOptions = new PreprocessorOptions
        {
            PredefinedMacros = _options.PredefinedMacros, // include dirs are handled by the resolver
            LexerOptions = new LexerOptions { AllowLineComments = _options.AllowLineComments },
        };

        var preprocessor = new Preprocessor(resolver, diagnostics, ppOptions);
        var tokens = preprocessor.Preprocess(source, path);

        var parser = new Parser(tokens, diagnostics);
        var ast = parser.ParseTranslationUnit();

        var model = SemanticAnalyzer.Analyze(ast, diagnostics);

        // Emit only when requested and the program is semantically sound; surface unsupported
        // constructs as diagnostics.
        string? emitted = null;
        if (_options.EmitCSharp && !diagnostics.HasErrors)
        {
            try { emitted = CSharpEmitter.Emit(ast, model); }
            catch (EmitException ex) { diagnostics.Error(new SourceLocation(path, 0, 0, 0), $"emit: {ex.Message}"); }
        }

        return new TranslationUnitResult(path, tokens, ast, model, emitted, diagnostics.ToList());
    }

    private static string? TryReadFile(string path, out string? error)
    {
        try
        {
            error = null;
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = ex.Message;
            return null;
        }
    }
}
