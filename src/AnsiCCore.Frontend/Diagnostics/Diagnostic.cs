namespace AnsiCCore.Frontend.Diagnostics;

/// <summary>Severity of a <see cref="Diagnostic"/>.</summary>
public enum DiagnosticSeverity
{
    Warning,
    Error,
}

/// <summary>A single compiler message anchored to a source location.</summary>
public sealed record Diagnostic(DiagnosticSeverity Severity, SourceLocation Location, string Message)
{
    public override string ToString() => $"{Location}: {Severity.ToString().ToLowerInvariant()}: {Message}";
}
