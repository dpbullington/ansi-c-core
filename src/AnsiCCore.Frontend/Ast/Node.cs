using AnsiCCore.Frontend.Diagnostics;

namespace AnsiCCore.Frontend.Ast;

/// <summary>
/// Base of every C syntax-tree node. The tree is a faithful <em>parse</em> tree: declarators
/// are recorded structurally and C types are not resolved here — that is Semantics' job. Every
/// node records the <see cref="SourceLocation"/> of the token that begins it.
/// </summary>
public abstract record Node(SourceLocation Location);

/// <summary>A complete translation unit: a sequence of <see cref="FunctionDefinition"/> and <see cref="Declaration"/>.</summary>
public sealed record TranslationUnit(SourceLocation Location, IReadOnlyList<Node> Items) : Node(Location);
