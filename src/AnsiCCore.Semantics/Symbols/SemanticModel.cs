using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Diagnostics;

namespace AnsiCCore.Semantics.Symbols;

/// <summary>
/// The result of semantic analysis: the global scope, all diagnostics, and a map from
/// declaring AST nodes (<see cref="InitDeclarator"/>, <see cref="FunctionDefinition"/>,
/// <see cref="ParameterDeclaration"/>) to the <see cref="Symbol"/> each introduced. Emit
/// consumes this to translate without re-deriving C semantics.
/// </summary>
public sealed class SemanticModel
{
    private readonly Dictionary<Node, Symbol> _symbolsByNode = new();

    public SemanticModel(Scope globalScope, IReadOnlyList<Diagnostic> diagnostics)
    {
        GlobalScope = globalScope;
        Diagnostics = diagnostics;
    }

    public Scope GlobalScope { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public bool Success => !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public IReadOnlyDictionary<Node, Symbol> SymbolsByNode => _symbolsByNode;

    internal void RecordSymbol(Node node, Symbol symbol) => _symbolsByNode[node] = symbol;
}
