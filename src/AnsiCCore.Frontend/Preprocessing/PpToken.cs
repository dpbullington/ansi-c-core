using System.Collections.Immutable;
using AnsiCCore.Frontend.Lexing;

namespace AnsiCCore.Frontend.Preprocessing;

/// <summary>
/// A token as seen by the preprocessor: the lexed <see cref="Token"/> plus a <em>hide set</em>
/// — the set of macro names that must not be re-expanded on this token. Hide sets are the
/// mechanism that stops macro self-reference and mutual recursion (Prosser's algorithm).
/// A placemarker is an empty pseudo-token used transiently by <c>##</c> pasting.
/// </summary>
internal readonly record struct PpToken(Token Token, ImmutableHashSet<string> HideSet, bool IsPlacemarker = false)
{
    public TokenKind Kind => Token.Kind;
    public string Text => Token.Text;

    public static PpToken From(Token token) => new(token, ImmutableHashSet<string>.Empty);
    public static PpToken Placemarker() => new(default, ImmutableHashSet<string>.Empty, IsPlacemarker: true);

    public PpToken WithHideSet(ImmutableHashSet<string> hideSet) => this with { HideSet = hideSet };

    public override string ToString() => IsPlacemarker ? "<placemarker>" : Token.ToString();
}

/// <summary>Shared predicates about preprocessing tokens.</summary>
internal static class PpFacts
{
    /// <summary>
    /// True for anything the preprocessor treats as an identifier. Keyword classification is
    /// a parser-phase concern (translation phase 7); during preprocessing a token spelled like
    /// a keyword is just an identifier that happens to match a keyword, and may name a macro.
    /// </summary>
    public static bool IsIdentifierLike(in Token t) => t.Kind == TokenKind.Identifier || t.IsKeyword;
}
