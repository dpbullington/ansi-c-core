using AnsiCCore.Frontend.Lexing;

namespace AnsiCCore.Frontend.Preprocessing;

/// <summary>Configuration for a <see cref="Preprocessor"/> run.</summary>
public sealed record PreprocessorOptions
{
    /// <summary>
    /// Command-line macro definitions (<c>-D</c>). A null or empty value is defined as <c>1</c>,
    /// matching the usual compiler convention; any other value is lexed as the replacement list.
    /// </summary>
    public IReadOnlyDictionary<string, string?> PredefinedMacros { get; init; }
        = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>Lexer options applied to the primary source and every included file.</summary>
    public LexerOptions LexerOptions { get; init; } = LexerOptions.Default;

    /// <summary>Guards against runaway <c>#include</c> cycles.</summary>
    public int MaxIncludeDepth { get; init; } = 200;

    /// <summary>Defensive cap on tokens produced by a single macro-expansion pass.</summary>
    public int MaxExpansionTokens { get; init; } = 2_000_000;

    /// <summary>Defines <c>__STDC__</c> as <c>1</c>.</summary>
    public bool DefineStdc { get; init; } = true;

    /// <summary>
    /// Timestamp used for <c>__DATE__</c> / <c>__TIME__</c>. When null the preprocessor uses the
    /// current local time; tests pass a fixed value for determinism.
    /// </summary>
    public DateTime? Clock { get; init; }

    public static PreprocessorOptions Default { get; } = new();
}
