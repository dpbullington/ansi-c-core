using AnsiCCore.Frontend.Diagnostics;

namespace AnsiCCore.Frontend.Lexing;

/// <summary>
/// A single lexical token. <see cref="Text"/> is the exact source lexeme (spelling); the
/// lexer does not decode numeric values or string/char escapes — later stages do.
/// </summary>
/// <remarks>
/// <see cref="AtStartOfLine"/> and <see cref="HasLeadingTrivia"/> carry the whitespace
/// context the preprocessor needs (directives must begin a line; function-like macro
/// invocation is sensitive to a space before <c>(</c>).
/// </remarks>
public readonly record struct Token(
    TokenKind Kind,
    string Text,
    SourceLocation Location,
    bool AtStartOfLine,
    bool HasLeadingTrivia)
{
    /// <summary>True for any of the 32 C89 keywords.</summary>
    public bool IsKeyword => Kind is >= TokenKind.KwAuto and <= TokenKind.KwWhile;

    public override string ToString() => $"{Kind} '{Text}' @ {Location}";
}
