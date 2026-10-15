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
    private readonly Dictionary<Expression, ExpressionType> _expressionTypes = new();
    private readonly Dictionary<TypeName, Types.QualType> _resolvedTypeNames = new();

    public SemanticModel(Scope globalScope, IReadOnlyList<Diagnostic> diagnostics)
    {
        GlobalScope = globalScope;
        Diagnostics = diagnostics;
    }

    public Scope GlobalScope { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public bool Success => !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public IReadOnlyDictionary<Node, Symbol> SymbolsByNode => _symbolsByNode;

    /// <summary>The type assigned to each expression node by type-checking.</summary>
    public IReadOnlyDictionary<Expression, ExpressionType> ExpressionTypes => _expressionTypes;

    /// <summary>The type of an expression, or null if it was not typed (e.g. inside an unparsed region).</summary>
    public ExpressionType? TypeOf(Expression expression)
        => _expressionTypes.TryGetValue(expression, out var t) ? t : null;

    /// <summary>The resolved type of a cast/sizeof type-name.</summary>
    public IReadOnlyDictionary<TypeName, Types.QualType> ResolvedTypeNames => _resolvedTypeNames;

    internal void RecordSymbol(Node node, Symbol symbol) => _symbolsByNode[node] = symbol;

    internal void RecordExpressionType(Expression expression, ExpressionType type) => _expressionTypes[expression] = type;

    internal void RecordTypeName(TypeName node, Types.QualType type) => _resolvedTypeNames[node] = type;
}
