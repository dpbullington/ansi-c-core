using System.Globalization;
using AnsiCCore.Frontend.Diagnostics;

namespace AnsiCCore.Frontend.Lexing;

/// <summary>
/// Options controlling lexer behavior.
/// </summary>
public sealed record LexerOptions
{
    /// <summary>
    /// Strict ANSI C89 has only <c>/* */</c> comments; <c>//</c> line comments are a C99
    /// feature. Default is strict (false). Enable only when deliberately relaxing the dialect.
    /// </summary>
    public bool AllowLineComments { get; init; }

    /// <summary>
    /// C89 translation phase 1 replaces the nine trigraph sequences (e.g. <c>??=</c> → <c>#</c>)
    /// everywhere, including inside string and character literals. On by default for conformance;
    /// disable to treat source as already past phase 1.
    /// </summary>
    public bool Trigraphs { get; init; } = true;

    public static LexerOptions Default { get; } = new();
}

/// <summary>
/// Hand-written, single-pass, maximal-munch lexer for ANSI C (C89).
/// </summary>
/// <remarks>
/// Scope: this lexer implements C translation phase 3 (tokenization). It assumes phases 1–2
/// (source-character mapping, trigraph replacement, and backslash-newline line splicing)
/// have been applied upstream, EXCEPT that a backslash immediately followed by a newline is
/// still tolerated here as whitespace / inside literals so stray continuations don't derail
/// tokenization. Comments are consumed as whitespace (not emitted as tokens).
/// </remarks>
public sealed class Lexer
{
    private readonly string _text;
    private readonly string _fileName;
    private readonly LexerOptions _options;

    private int _pos;
    private int _line = 1;
    private int _lineStart;        // offset of the first char of the current line

    private bool _atStartOfLine = true;
    private bool _hasLeadingTrivia;

    public Lexer(string text, string fileName = "<source>", DiagnosticBag? diagnostics = null, LexerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        _fileName = fileName;
        Diagnostics = diagnostics ?? new DiagnosticBag();
        _options = options ?? LexerOptions.Default;
        _text = _options.Trigraphs ? TranslateTrigraphs(text) : text; // C89 phase 1
    }

    /// <summary>Replaces the nine C89 trigraph sequences (<c>?? </c> + a punctuator), left to right.</summary>
    private static string TranslateTrigraphs(string text)
    {
        int first = text.IndexOf('?');
        if (first < 0 || first + 2 >= text.Length) return text; // no room for a '??x' sequence

        var sb = new System.Text.StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (i + 2 < text.Length && text[i] == '?' && text[i + 1] == '?')
            {
                char r = text[i + 2] switch
                {
                    '=' => '#', '(' => '[', ')' => ']', '/' => '\\', '\'' => '^',
                    '<' => '{', '>' => '}', '!' => '|', '-' => '~', _ => '\0',
                };
                if (r != '\0') { sb.Append(r); i += 3; continue; }
            }
            sb.Append(text[i]);
            i++;
        }
        return sb.ToString();
    }

    public DiagnosticBag Diagnostics { get; }

    /// <summary>
    /// Tokenizes the entire input. The returned list always ends with a single
    /// <see cref="TokenKind.EndOfFile"/> token.
    /// </summary>
    public IReadOnlyList<Token> Tokenize()
    {
        var tokens = new List<Token>();
        Token token;
        do
        {
            token = NextToken();
            tokens.Add(token);
        }
        while (token.Kind != TokenKind.EndOfFile);
        return tokens;
    }

    // --- character cursor -------------------------------------------------

    private bool AtEnd => _pos >= _text.Length;
    private char Current => _pos < _text.Length ? _text[_pos] : '\0';
    private char Peek(int ahead = 1) => _pos + ahead < _text.Length ? _text[_pos + ahead] : '\0';

    private char Advance()
    {
        char c = _text[_pos++];
        // Track line starts. Treat "\r\n", lone "\n", and lone "\r" each as one newline.
        if (c == '\n' || (c == '\r' && Current != '\n'))
        {
            _line++;
            _lineStart = _pos;
        }
        return c;
    }

    private SourceLocation LocationHere()
        => new(_fileName, _line, _pos - _lineStart + 1, _pos);

    // --- main dispatch ----------------------------------------------------

    private Token NextToken()
    {
        SkipTrivia();

        var start = LocationHere();
        bool atStartOfLine = _atStartOfLine;
        bool hasLeadingTrivia = _hasLeadingTrivia;
        _atStartOfLine = false;
        _hasLeadingTrivia = false;

        if (AtEnd)
            return Make(TokenKind.EndOfFile, start.Offset, start, atStartOfLine, hasLeadingTrivia);

        char c = Current;

        // Wide-literal prefix: L'...' / L"..." (an "L" glued to a quote).
        if (c == 'L' && (Peek() == '\'' || Peek() == '"'))
        {
            Advance(); // consume L
            return Current == '\''
                ? ScanCharacter(start, atStartOfLine, hasLeadingTrivia)
                : ScanString(start, atStartOfLine, hasLeadingTrivia);
        }

        if (IsIdentifierStart(c))
            return ScanIdentifierOrKeyword(start, atStartOfLine, hasLeadingTrivia);

        if (char.IsAsciiDigit(c) || (c == '.' && char.IsAsciiDigit(Peek())))
            return ScanNumber(start, atStartOfLine, hasLeadingTrivia);

        if (c == '\'')
            return ScanCharacter(start, atStartOfLine, hasLeadingTrivia);

        if (c == '"')
            return ScanString(start, atStartOfLine, hasLeadingTrivia);

        return ScanPunctuator(start, atStartOfLine, hasLeadingTrivia);
    }

    private Token Make(TokenKind kind, int startOffset, SourceLocation start, bool atStartOfLine, bool hasLeadingTrivia)
    {
        string text = kind == TokenKind.EndOfFile ? string.Empty : _text[startOffset.._pos];
        return new Token(kind, text, start, atStartOfLine, hasLeadingTrivia);
    }

    // --- trivia (whitespace + comments) -----------------------------------

    private void SkipTrivia()
    {
        while (!AtEnd)
        {
            char c = Current;

            if (c is ' ' or '\t' or '\v' or '\f')
            {
                _hasLeadingTrivia = true;
                Advance();
            }
            else if (c is '\n' or '\r')
            {
                _atStartOfLine = true;
                _hasLeadingTrivia = true;
                Advance();
            }
            else if (c == '\\' && Peek() is '\n' or '\r')
            {
                // Tolerated line continuation between tokens: not a new logical line.
                _hasLeadingTrivia = true;
                Advance(); // backslash
                Advance(); // newline
            }
            else if (c == '/' && Peek() == '*')
            {
                SkipBlockComment();
                _hasLeadingTrivia = true;
            }
            else if (c == '/' && Peek() == '/' && _options.AllowLineComments)
            {
                while (!AtEnd && Current is not ('\n' or '\r'))
                    Advance();
                _hasLeadingTrivia = true;
            }
            else
            {
                break;
            }
        }
    }

    private void SkipBlockComment()
    {
        var start = LocationHere();
        Advance(); // '/'
        Advance(); // '*'
        while (!AtEnd)
        {
            if (Current == '*' && Peek() == '/')
            {
                Advance();
                Advance();
                return;
            }
            Advance();
        }
        Diagnostics.Error(start, "unterminated block comment");
    }

    // --- identifiers / keywords -------------------------------------------

    private Token ScanIdentifierOrKeyword(SourceLocation start, bool atStartOfLine, bool hasLeadingTrivia)
    {
        int begin = _pos;
        while (!AtEnd && IsIdentifierContinue(Current))
            Advance();

        string text = _text[begin.._pos];
        TokenKind kind = Keywords.GetValueOrDefault(text, TokenKind.Identifier);
        return new Token(kind, text, start, atStartOfLine, hasLeadingTrivia);
    }

    // --- numbers ----------------------------------------------------------

    private Token ScanNumber(SourceLocation start, bool atStartOfLine, bool hasLeadingTrivia)
    {
        int begin = _pos;
        bool isFloat = false;

        if (Current == '0' && (Peek() is 'x' or 'X'))
        {
            Advance(); // 0
            Advance(); // x
            int hexDigits = 0;
            while (IsHexDigit(Current)) { Advance(); hexDigits++; }
            if (hexDigits == 0)
                Diagnostics.Error(start, "hexadecimal constant has no digits");
            ScanIntegerSuffix();
        }
        else
        {
            while (char.IsAsciiDigit(Current)) Advance();

            if (Current == '.')
            {
                isFloat = true;
                Advance();
                while (char.IsAsciiDigit(Current)) Advance();
            }

            if (Current is 'e' or 'E')
            {
                isFloat = true;
                Advance();
                if (Current is '+' or '-') Advance();
                int expDigits = 0;
                while (char.IsAsciiDigit(Current)) { Advance(); expDigits++; }
                if (expDigits == 0)
                    Diagnostics.Error(start, "exponent has no digits");
            }

            if (isFloat) ScanFloatSuffix();
            else ScanIntegerSuffix();
        }

        // A numeric lexeme may not run straight into identifier characters.
        if (IsIdentifierStart(Current))
        {
            int junkStart = _pos;
            while (!AtEnd && IsIdentifierContinue(Current)) Advance();
            Diagnostics.Error(start, $"invalid suffix '{_text[junkStart.._pos]}' on {(isFloat ? "floating" : "integer")} constant");
        }

        var kind = isFloat ? TokenKind.FloatingConstant : TokenKind.IntegerConstant;
        return new Token(kind, _text[begin.._pos], start, atStartOfLine, hasLeadingTrivia);
    }

    private void ScanIntegerSuffix()
    {
        // Accept any run of u/U/l/L; precise validity (at most one 'u', at most "ll") is a
        // later concern. Enough here to keep the suffix glued to the token.
        while (Current is 'u' or 'U' or 'l' or 'L') Advance();
    }

    private void ScanFloatSuffix()
    {
        if (Current is 'f' or 'F' or 'l' or 'L') Advance();
    }

    // --- character / string literals --------------------------------------

    private Token ScanCharacter(SourceLocation start, bool atStartOfLine, bool hasLeadingTrivia)
    {
        int begin = _pos;
        Advance(); // opening '
        int contentStart = _pos;

        while (!AtEnd && Current != '\'')
        {
            if (Current is '\n' or '\r')
            {
                Diagnostics.Error(start, "unterminated character constant");
                return new Token(TokenKind.CharacterConstant, _text[begin.._pos], start, atStartOfLine, hasLeadingTrivia);
            }
            ConsumeCharacterOrEscape();
        }

        if (AtEnd)
            Diagnostics.Error(start, "unterminated character constant");
        else
        {
            if (_pos == contentStart)
                Diagnostics.Error(start, "empty character constant");
            Advance(); // closing '
        }

        return new Token(TokenKind.CharacterConstant, _text[begin.._pos], start, atStartOfLine, hasLeadingTrivia);
    }

    private Token ScanString(SourceLocation start, bool atStartOfLine, bool hasLeadingTrivia)
    {
        int begin = _pos;
        Advance(); // opening "

        while (!AtEnd && Current != '"')
        {
            if (Current is '\n' or '\r')
            {
                Diagnostics.Error(start, "unterminated string literal");
                return new Token(TokenKind.StringLiteral, _text[begin.._pos], start, atStartOfLine, hasLeadingTrivia);
            }
            ConsumeCharacterOrEscape();
        }

        if (AtEnd)
            Diagnostics.Error(start, "unterminated string literal");
        else
            Advance(); // closing "

        return new Token(TokenKind.StringLiteral, _text[begin.._pos], start, atStartOfLine, hasLeadingTrivia);
    }

    /// <summary>
    /// Consumes one element of a char/string literal body: either a backslash escape (with
    /// tolerated line continuation) or a single ordinary character. Escape-sequence meaning
    /// is validated/decoded later; here we only ensure the backslash claims its next char.
    /// </summary>
    private void ConsumeCharacterOrEscape()
    {
        if (Current == '\\')
        {
            Advance(); // backslash
            if (Current is '\n' or '\r') // line continuation inside a literal
                Advance();
            else if (!AtEnd)
                Advance(); // the escaped character (\n, \", \\, \123, \xAB, ... all claim >=1 char)
        }
        else
        {
            Advance();
        }
    }

    // --- punctuators ------------------------------------------------------

    private Token ScanPunctuator(SourceLocation start, bool atStartOfLine, bool hasLeadingTrivia)
    {
        int begin = _pos;
        char c = Advance();
        TokenKind kind;

        switch (c)
        {
            case '[': kind = TokenKind.LBracket; break;
            case ']': kind = TokenKind.RBracket; break;
            case '(': kind = TokenKind.LParen; break;
            case ')': kind = TokenKind.RParen; break;
            case '{': kind = TokenKind.LBrace; break;
            case '}': kind = TokenKind.RBrace; break;
            case '~': kind = TokenKind.Tilde; break;
            case '?': kind = TokenKind.Question; break;
            case ':': kind = TokenKind.Colon; break;
            case ';': kind = TokenKind.Semicolon; break;
            case ',': kind = TokenKind.Comma; break;

            case '.':
                if (Current == '.' && Peek() == '.') { Advance(); Advance(); kind = TokenKind.Ellipsis; }
                else kind = TokenKind.Dot;
                break;

            case '+':
                if (Current == '+') { Advance(); kind = TokenKind.PlusPlus; }
                else if (Current == '=') { Advance(); kind = TokenKind.PlusEqual; }
                else kind = TokenKind.Plus;
                break;

            case '-':
                if (Current == '-') { Advance(); kind = TokenKind.MinusMinus; }
                else if (Current == '=') { Advance(); kind = TokenKind.MinusEqual; }
                else if (Current == '>') { Advance(); kind = TokenKind.Arrow; }
                else kind = TokenKind.Minus;
                break;

            case '*':
                if (Current == '=') { Advance(); kind = TokenKind.StarEqual; }
                else kind = TokenKind.Star;
                break;

            case '/':
                if (Current == '=') { Advance(); kind = TokenKind.SlashEqual; }
                else kind = TokenKind.Slash;
                break;

            case '%':
                if (Current == '=') { Advance(); kind = TokenKind.PercentEqual; }
                else kind = TokenKind.Percent;
                break;

            case '&':
                if (Current == '&') { Advance(); kind = TokenKind.AmpAmp; }
                else if (Current == '=') { Advance(); kind = TokenKind.AmpEqual; }
                else kind = TokenKind.Ampersand;
                break;

            case '|':
                if (Current == '|') { Advance(); kind = TokenKind.PipePipe; }
                else if (Current == '=') { Advance(); kind = TokenKind.PipeEqual; }
                else kind = TokenKind.Pipe;
                break;

            case '^':
                if (Current == '=') { Advance(); kind = TokenKind.CaretEqual; }
                else kind = TokenKind.Caret;
                break;

            case '=':
                if (Current == '=') { Advance(); kind = TokenKind.EqualEqual; }
                else kind = TokenKind.Equal;
                break;

            case '!':
                if (Current == '=') { Advance(); kind = TokenKind.BangEqual; }
                else kind = TokenKind.Bang;
                break;

            case '<':
                if (Current == '<' && Peek() == '=') { Advance(); Advance(); kind = TokenKind.LessLessEqual; }
                else if (Current == '<') { Advance(); kind = TokenKind.LessLess; }
                else if (Current == '=') { Advance(); kind = TokenKind.LessEqual; }
                else kind = TokenKind.Less;
                break;

            case '>':
                if (Current == '>' && Peek() == '=') { Advance(); Advance(); kind = TokenKind.GreaterGreaterEqual; }
                else if (Current == '>') { Advance(); kind = TokenKind.GreaterGreater; }
                else if (Current == '=') { Advance(); kind = TokenKind.GreaterEqual; }
                else kind = TokenKind.Greater;
                break;

            case '#':
                if (Current == '#') { Advance(); kind = TokenKind.HashHash; }
                else kind = TokenKind.Hash;
                break;

            default:
                Diagnostics.Error(start, $"stray '{DescribeChar(c)}' in program");
                kind = TokenKind.Unknown;
                break;
        }

        return new Token(kind, _text[begin.._pos], start, atStartOfLine, hasLeadingTrivia);
    }

    // --- helpers ----------------------------------------------------------

    private static bool IsIdentifierStart(char c) => char.IsAsciiLetter(c) || c == '_';
    private static bool IsIdentifierContinue(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
    private static bool IsHexDigit(char c) => char.IsAsciiHexDigit(c);

    private static string DescribeChar(char c)
        => char.IsControl(c) ? $"\\x{(int)c:x2}" : c.ToString(CultureInfo.InvariantCulture);

    private static readonly IReadOnlyDictionary<string, TokenKind> Keywords = new Dictionary<string, TokenKind>(StringComparer.Ordinal)
    {
        ["auto"] = TokenKind.KwAuto,
        ["break"] = TokenKind.KwBreak,
        ["case"] = TokenKind.KwCase,
        ["char"] = TokenKind.KwChar,
        ["const"] = TokenKind.KwConst,
        ["continue"] = TokenKind.KwContinue,
        ["default"] = TokenKind.KwDefault,
        ["do"] = TokenKind.KwDo,
        ["double"] = TokenKind.KwDouble,
        ["else"] = TokenKind.KwElse,
        ["enum"] = TokenKind.KwEnum,
        ["extern"] = TokenKind.KwExtern,
        ["float"] = TokenKind.KwFloat,
        ["for"] = TokenKind.KwFor,
        ["goto"] = TokenKind.KwGoto,
        ["if"] = TokenKind.KwIf,
        ["int"] = TokenKind.KwInt,
        ["long"] = TokenKind.KwLong,
        ["register"] = TokenKind.KwRegister,
        ["return"] = TokenKind.KwReturn,
        ["short"] = TokenKind.KwShort,
        ["signed"] = TokenKind.KwSigned,
        ["sizeof"] = TokenKind.KwSizeof,
        ["static"] = TokenKind.KwStatic,
        ["struct"] = TokenKind.KwStruct,
        ["switch"] = TokenKind.KwSwitch,
        ["typedef"] = TokenKind.KwTypedef,
        ["union"] = TokenKind.KwUnion,
        ["unsigned"] = TokenKind.KwUnsigned,
        ["void"] = TokenKind.KwVoid,
        ["volatile"] = TokenKind.KwVolatile,
        ["while"] = TokenKind.KwWhile,
    };
}
