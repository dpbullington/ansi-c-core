using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;
using AnsiCCore.Semantics.Symbols;

namespace AnsiCCore.Driver;

/// <summary>The outcome of processing a single translation unit through the pipeline.</summary>
public sealed record TranslationUnitResult(
    string Path,
    IReadOnlyList<Token> Tokens,
    TranslationUnit? Ast,
    SemanticModel? Model,
    string? EmittedCSharp,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// The outcome of a whole driver run. Multiple translation units are compiled as one program:
/// their ASTs are analyzed together (<see cref="Model"/>) and emitted as one C# compilation unit
/// (<see cref="EmittedProgram"/>), so external functions and globals link across files.
/// </summary>
public sealed record DriverResult(
    IReadOnlyList<TranslationUnitResult> Units,
    SemanticModel? Model,
    string? EmittedProgram,
    IReadOnlyList<Diagnostic> ProgramDiagnostics)
{
    public IEnumerable<Diagnostic> AllDiagnostics => Units.SelectMany(u => u.Diagnostics).Concat(ProgramDiagnostics);

    /// <summary>True when nothing produced an error-severity diagnostic.</summary>
    public bool Success => !AllDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
