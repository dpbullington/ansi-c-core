using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;
using AnsiCCore.Semantics.Symbols;

namespace AnsiCCore.Driver;

/// <summary>The outcome of processing a single translation unit through the Frontend and Semantics.</summary>
public sealed record TranslationUnitResult(
    string Path,
    IReadOnlyList<Token> Tokens,
    TranslationUnit? Ast,
    SemanticModel? Model,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>The outcome of a whole driver run.</summary>
public sealed record DriverResult(IReadOnlyList<TranslationUnitResult> Units)
{
    public IEnumerable<Diagnostic> AllDiagnostics => Units.SelectMany(u => u.Diagnostics);

    /// <summary>True when no unit produced an error-severity diagnostic.</summary>
    public bool Success => !AllDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
