using AnsiCCore.Frontend.Diagnostics;

namespace AnsiCCore.Frontend.Ast;

/// <summary>Base of all statement nodes.</summary>
public abstract record Statement(SourceLocation Location) : Node(Location);

/// <summary>
/// A brace-enclosed block. <see cref="Items"/> are <see cref="Declaration"/> and
/// <see cref="Statement"/> nodes in source order (C89 requires declarations first, but the
/// parser records whatever order appears and leaves the rule to Semantics).
/// </summary>
public sealed record CompoundStatement(SourceLocation Location, IReadOnlyList<Node> Items) : Statement(Location);

/// <summary>An expression statement; <see cref="Expression"/> is null for the empty statement <c>;</c>.</summary>
public sealed record ExpressionStatement(SourceLocation Location, Expression? Expression) : Statement(Location);

/// <summary><c>if (condition) then [else otherwise]</c>.</summary>
public sealed record IfStatement(SourceLocation Location, Expression Condition, Statement Then, Statement? Otherwise) : Statement(Location);

/// <summary><c>switch (expression) body</c>.</summary>
public sealed record SwitchStatement(SourceLocation Location, Expression Expression, Statement Body) : Statement(Location);

/// <summary><c>while (condition) body</c>.</summary>
public sealed record WhileStatement(SourceLocation Location, Expression Condition, Statement Body) : Statement(Location);

/// <summary><c>do body while (condition);</c>.</summary>
public sealed record DoStatement(SourceLocation Location, Statement Body, Expression Condition) : Statement(Location);

/// <summary><c>for (init; condition; iteration) body</c> — each of the three clauses may be null.</summary>
public sealed record ForStatement(
    SourceLocation Location,
    Expression? Initializer,
    Expression? Condition,
    Expression? Iteration,
    Statement Body) : Statement(Location);

/// <summary>A named label <c>label: statement</c>.</summary>
public sealed record LabeledStatement(SourceLocation Location, string Label, Statement Statement) : Statement(Location);

/// <summary><c>case value: statement</c>.</summary>
public sealed record CaseStatement(SourceLocation Location, Expression Value, Statement Statement) : Statement(Location);

/// <summary><c>default: statement</c>.</summary>
public sealed record DefaultStatement(SourceLocation Location, Statement Statement) : Statement(Location);

/// <summary><c>goto label;</c>.</summary>
public sealed record GotoStatement(SourceLocation Location, string Label) : Statement(Location);

/// <summary><c>continue;</c>.</summary>
public sealed record ContinueStatement(SourceLocation Location) : Statement(Location);

/// <summary><c>break;</c>.</summary>
public sealed record BreakStatement(SourceLocation Location) : Statement(Location);

/// <summary><c>return [value];</c>.</summary>
public sealed record ReturnStatement(SourceLocation Location, Expression? Value) : Statement(Location);
