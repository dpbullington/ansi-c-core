namespace AnsiCCore.Frontend.Lexing;

/// <summary>
/// Canonical source spellings for token kinds that have a fixed one — keywords and
/// punctuators. Kinds whose text varies (identifiers, constants) and the sentinels return
/// the empty string. Useful wherever a <see cref="TokenKind"/> must be rendered back to C
/// (diagnostics, AST dumps, the emitter).
/// </summary>
public static class TokenSpelling
{
    public static string Spell(TokenKind kind)
    {
        string? punct = kind switch
        {
            TokenKind.LBracket => "[",
            TokenKind.RBracket => "]",
            TokenKind.LParen => "(",
            TokenKind.RParen => ")",
            TokenKind.LBrace => "{",
            TokenKind.RBrace => "}",
            TokenKind.Dot => ".",
            TokenKind.Arrow => "->",
            TokenKind.PlusPlus => "++",
            TokenKind.MinusMinus => "--",
            TokenKind.Ampersand => "&",
            TokenKind.Star => "*",
            TokenKind.Plus => "+",
            TokenKind.Minus => "-",
            TokenKind.Tilde => "~",
            TokenKind.Bang => "!",
            TokenKind.Slash => "/",
            TokenKind.Percent => "%",
            TokenKind.LessLess => "<<",
            TokenKind.GreaterGreater => ">>",
            TokenKind.Less => "<",
            TokenKind.Greater => ">",
            TokenKind.LessEqual => "<=",
            TokenKind.GreaterEqual => ">=",
            TokenKind.EqualEqual => "==",
            TokenKind.BangEqual => "!=",
            TokenKind.Caret => "^",
            TokenKind.Pipe => "|",
            TokenKind.AmpAmp => "&&",
            TokenKind.PipePipe => "||",
            TokenKind.Question => "?",
            TokenKind.Colon => ":",
            TokenKind.Semicolon => ";",
            TokenKind.Ellipsis => "...",
            TokenKind.Equal => "=",
            TokenKind.StarEqual => "*=",
            TokenKind.SlashEqual => "/=",
            TokenKind.PercentEqual => "%=",
            TokenKind.PlusEqual => "+=",
            TokenKind.MinusEqual => "-=",
            TokenKind.LessLessEqual => "<<=",
            TokenKind.GreaterGreaterEqual => ">>=",
            TokenKind.AmpEqual => "&=",
            TokenKind.CaretEqual => "^=",
            TokenKind.PipeEqual => "|=",
            TokenKind.Comma => ",",
            TokenKind.Hash => "#",
            TokenKind.HashHash => "##",
            _ => null,
        };
        if (punct != null) return punct;

        // Keywords: enum members are named Kw<Spelling>, lowercased.
        string name = kind.ToString();
        if (name.StartsWith("Kw", StringComparison.Ordinal))
            return name[2..].ToLowerInvariant();

        return string.Empty;
    }
}
