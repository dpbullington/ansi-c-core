using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;

namespace AnsiCCore.Frontend.Preprocessing;

/// <summary>
/// A <c>#define</c>d macro. Object-like macros have <see cref="IsFunctionLike"/> == false and
/// an empty <see cref="Parameters"/> list; function-like macros carry their (possibly empty)
/// parameter list. <see cref="Replacement"/> holds the body as lexed tokens.
/// </summary>
public sealed class Macro
{
    public Macro(string name, bool isFunctionLike, IReadOnlyList<string> parameters, IReadOnlyList<Token> replacement, SourceLocation definedAt)
    {
        Name = name;
        IsFunctionLike = isFunctionLike;
        Parameters = parameters;
        Replacement = replacement;
        DefinedAt = definedAt;
    }

    public string Name { get; }
    public bool IsFunctionLike { get; }
    public IReadOnlyList<string> Parameters { get; }
    public IReadOnlyList<Token> Replacement { get; }
    public SourceLocation DefinedAt { get; }

    /// <summary>
    /// C89 §3.8.3: a macro may be redefined only by an <em>identical</em> definition — same
    /// kind, same parameter spellings, and a replacement list matching token-for-token with
    /// identical intervening whitespace. Used to warn on incompatible redefinition.
    /// </summary>
    public bool IsIdenticalTo(Macro other)
    {
        if (IsFunctionLike != other.IsFunctionLike) return false;
        if (!Parameters.SequenceEqual(other.Parameters, StringComparer.Ordinal)) return false;
        if (Replacement.Count != other.Replacement.Count) return false;

        for (int i = 0; i < Replacement.Count; i++)
        {
            var a = Replacement[i];
            var b = other.Replacement[i];
            if (a.Kind != b.Kind || !string.Equals(a.Text, b.Text, StringComparison.Ordinal))
                return false;
            // Whitespace separation must match, except there is never leading whitespace
            // significance on the first replacement token.
            if (i > 0 && a.HasLeadingTrivia != b.HasLeadingTrivia)
                return false;
        }

        return true;
    }
}
