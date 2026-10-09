using System.Collections;

namespace AnsiCCore.Frontend.Diagnostics;

/// <summary>
/// Accumulates diagnostics produced by a stage. Shared across Frontend stages so a single
/// run can gather lexical, preprocessing, and parse messages into one ordered list.
/// </summary>
public sealed class DiagnosticBag : IReadOnlyCollection<Diagnostic>
{
    private readonly List<Diagnostic> _items = [];

    public int Count => _items.Count;
    public bool HasErrors => _items.Any(d => d.Severity == DiagnosticSeverity.Error);

    public void Report(DiagnosticSeverity severity, SourceLocation location, string message)
        => _items.Add(new Diagnostic(severity, location, message));

    public void Error(SourceLocation location, string message)
        => Report(DiagnosticSeverity.Error, location, message);

    public void Warning(SourceLocation location, string message)
        => Report(DiagnosticSeverity.Warning, location, message);

    public IEnumerator<Diagnostic> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
