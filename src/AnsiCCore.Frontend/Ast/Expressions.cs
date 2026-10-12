using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;

namespace AnsiCCore.Frontend.Ast;

/// <summary>Base of all expression nodes.</summary>
public abstract record Expression(SourceLocation Location) : Node(Location);

/// <summary>A reference to an identifier (variable, function, or enum constant — unresolved here).</summary>
public sealed record IdentifierExpression(SourceLocation Location, string Name) : Expression(Location);

/// <summary>An integer constant; <see cref="Text"/> is the raw lexeme (suffix/base preserved).</summary>
public sealed record IntegerLiteral(SourceLocation Location, string Text) : Expression(Location);

/// <summary>A floating constant; <see cref="Text"/> is the raw lexeme.</summary>
public sealed record FloatingLiteral(SourceLocation Location, string Text) : Expression(Location);

/// <summary>A character constant; <see cref="Text"/> is the raw lexeme including quotes/prefix.</summary>
public sealed record CharacterLiteral(SourceLocation Location, string Text) : Expression(Location);

/// <summary>
/// One or more adjacent string-literal tokens (translation phase 6 concatenation). Each part is
/// a raw lexeme including its quotes and optional <c>L</c> prefix.
/// </summary>
public sealed record StringLiteral(SourceLocation Location, IReadOnlyList<string> Parts) : Expression(Location);

/// <summary>A function call <c>callee(args...)</c>.</summary>
public sealed record CallExpression(SourceLocation Location, Expression Callee, IReadOnlyList<Expression> Arguments) : Expression(Location);

/// <summary>An array subscript <c>array[index]</c>.</summary>
public sealed record IndexExpression(SourceLocation Location, Expression Array, Expression Index) : Expression(Location);

/// <summary>Member access <c>target.member</c> (<see cref="Arrow"/> false) or <c>target->member</c> (true).</summary>
public sealed record MemberExpression(SourceLocation Location, Expression Target, string Member, bool Arrow) : Expression(Location);

/// <summary>A prefix unary expression. <see cref="Operator"/> is the operator token kind.</summary>
public sealed record UnaryExpression(SourceLocation Location, TokenKind Operator, Expression Operand) : Expression(Location);

/// <summary>A postfix <c>++</c>/<c>--</c> expression.</summary>
public sealed record PostfixExpression(SourceLocation Location, TokenKind Operator, Expression Operand) : Expression(Location);

/// <summary><c>sizeof</c> applied to an expression operand.</summary>
public sealed record SizeOfExpression(SourceLocation Location, Expression Operand) : Expression(Location);

/// <summary><c>sizeof ( type-name )</c>.</summary>
public sealed record SizeOfTypeExpression(SourceLocation Location, TypeName Type) : Expression(Location);

/// <summary>A cast <c>( type-name ) operand</c>.</summary>
public sealed record CastExpression(SourceLocation Location, TypeName Type, Expression Operand) : Expression(Location);

/// <summary>A binary expression; <see cref="Operator"/> is the operator token kind.</summary>
public sealed record BinaryExpression(SourceLocation Location, TokenKind Operator, Expression Left, Expression Right) : Expression(Location);

/// <summary>The ternary conditional <c>condition ? whenTrue : whenFalse</c>.</summary>
public sealed record ConditionalExpression(SourceLocation Location, Expression Condition, Expression WhenTrue, Expression WhenFalse) : Expression(Location);

/// <summary>An assignment; <see cref="Operator"/> is <c>=</c> or a compound-assignment token kind.</summary>
public sealed record AssignmentExpression(SourceLocation Location, TokenKind Operator, Expression Target, Expression Value) : Expression(Location);

/// <summary>A comma expression <c>left , right</c>.</summary>
public sealed record CommaExpression(SourceLocation Location, Expression Left, Expression Right) : Expression(Location);
