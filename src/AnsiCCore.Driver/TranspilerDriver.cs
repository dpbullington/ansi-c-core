using System.Reflection;
using AnsiCCore.Emit;
using AnsiCCore.Frontend.Ast;
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
    private ExportRegistry? _registry;
    private readonly List<string> _linkErrors = [];

    public TranspilerDriver(DriverOptions? options = null) => _options = options ?? DriverOptions.Default;

    /// <summary>Builds the import library once: default libc plus each successfully loaded <c>--link</c> assembly.</summary>
    private ExportRegistry GetExportRegistry()
    {
        if (_registry is not null) return _registry;

        var assemblies = new List<Assembly>();
        foreach (var path in _options.LinkedAssemblies)
        {
            try { assemblies.Add(Assembly.LoadFrom(Path.GetFullPath(path))); }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or ArgumentException)
            {
                _linkErrors.Add($"cannot link '{path}': {ex.Message}");
            }
        }
        return _registry = ExportRegistry.FromLinkedAssemblies(assemblies);
    }

    /// <summary>
    /// Reads and processes each input path. All files are compiled as one program: each is
    /// preprocessed and parsed independently, their ASTs are concatenated, and the combined unit
    /// is analyzed and emitted once — so external functions and globals link across files.
    /// (Identical type definitions from a shared header are deduplicated by Semantics.)
    /// </summary>
    public DriverResult Run(IEnumerable<string> inputPaths)
    {
        var resolver = new FileSystemIncludeResolver(_options.IncludeDirectories, _options.IncludeDirectories);
        var ppOptions = new PreprocessorOptions
        {
            PredefinedMacros = _options.PredefinedMacros,
            LexerOptions = new LexerOptions { AllowLineComments = _options.AllowLineComments },
        };

        var units = new List<TranslationUnitResult>();
        var items = new List<Node>();
        var fileOf = new Dictionary<Node, int>();
        SourceLocation start = default;
        bool haveStart = false;
        int fileIndex = 0;

        foreach (var path in inputPaths)
        {
            var bag = new DiagnosticBag(); // per-file: lexer/preprocessor/parser messages
            string? source = TryReadFile(path, out var readError);
            if (source == null)
            {
                bag.Error(new SourceLocation(path, 0, 0, 0), readError ?? "could not read file");
                units.Add(new TranslationUnitResult(path, [], null, null, null, bag.ToList()));
                continue;
            }

            var tokens = new Preprocessor(resolver, bag, ppOptions).Preprocess(source, path);
            var ast = new Parser(tokens, bag).ParseTranslationUnit();
            if (!haveStart) { start = ast.Location; haveStart = true; }
            foreach (var item in ast.Items) fileOf[item] = fileIndex;
            items.AddRange(ast.Items);
            units.Add(new TranslationUnitResult(path, tokens, ast, null, null, bag.ToList()));
            fileIndex++;
        }

        // Analyze and emit the whole program as one unit; static symbols stay isolated per file.
        var program = new TranslationUnit(start, items);
        var programDiagnostics = new DiagnosticBag();
        var model = SemanticAnalyzer.Analyze(program, programDiagnostics, fileOf);

        string? emitted = null;
        if (_options.EmitCSharp)
        {
            var registry = GetExportRegistry();
            foreach (var error in _linkErrors)
                programDiagnostics.Error(start, error);

            bool anyFileError = units.Any(u => u.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
            if (!programDiagnostics.HasErrors && !anyFileError)
            {
                try { emitted = CSharpEmitter.Emit(program, model, registry); }
                catch (EmitException ex) { programDiagnostics.Error(start, $"emit: {ex.Message}"); }
            }
        }

        return new DriverResult(units, model, emitted, programDiagnostics.ToList());
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
        if (_options.EmitCSharp)
        {
            var registry = GetExportRegistry();
            foreach (var error in _linkErrors)
                diagnostics.Error(new SourceLocation(path, 0, 0, 0), error);

            if (!diagnostics.HasErrors)
            {
                try { emitted = CSharpEmitter.Emit(ast, model, registry); }
                catch (EmitException ex) { diagnostics.Error(new SourceLocation(path, 0, 0, 0), $"emit: {ex.Message}"); }
            }
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
