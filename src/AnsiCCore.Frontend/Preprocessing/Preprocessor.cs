using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;

namespace AnsiCCore.Frontend.Preprocessing;

/// <summary>
/// The C89 preprocessor (translation phase 4): executes directives, expands macros, and
/// splices included files, turning a lexed token stream into the token stream the parser
/// consumes. Expansion uses hide sets (Prosser's algorithm) so recursive and mutually
/// recursive macros terminate exactly as the standard requires.
/// </summary>
/// <remarks>
/// Keyword classification is left intact on the output tokens for the parser, but internally
/// any keyword-spelled token is treated as an identifier (see <see cref="PpFacts"/>), because
/// keywords do not exist yet at preprocessing time.
/// </remarks>
public sealed class Preprocessor
{
    private sealed class CondState
    {
        public bool ParentActive;   // was the enclosing region active?
        public bool BranchTaken;    // has any branch of this #if chain been taken?
        public bool Active;         // is the current branch active?
        public bool SeenElse;       // has #else been seen (so no more #elif/#else)?
    }

    private readonly IIncludeResolver _resolver;
    private readonly PreprocessorOptions _options;
    private readonly MacroTable _macros = new();
    private readonly List<Token> _output = [];
    private readonly Stack<CondState> _conditionals = new();
    private readonly string _dateMacro;
    private readonly string _timeMacro;

    public Preprocessor(IIncludeResolver resolver, DiagnosticBag? diagnostics = null, PreprocessorOptions? options = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _options = options ?? PreprocessorOptions.Default;
        Diagnostics = diagnostics ?? new DiagnosticBag();

        var now = _options.Clock ?? DateTime.Now;
        _dateMacro = $"\"{now.ToString("MMM", CultureInfo.InvariantCulture)} {now.Day,2} {now.Year}\"";
        _timeMacro = $"\"{now:HH:mm:ss}\"";

        PredefineMacros();
    }

    public DiagnosticBag Diagnostics { get; }

    /// <summary>Preprocesses <paramref name="source"/> and returns the resulting token stream (ending in EOF).</summary>
    public IReadOnlyList<Token> Preprocess(string source, string fileName = "<source>")
    {
        var tokens = new Lexer(source, fileName, Diagnostics, _options.LexerOptions).Tokenize();
        Run(tokens, DirectoryOf(fileName), includeDepth: 0);

        if (_conditionals.Count > 0)
            Diagnostics.Error(default, "unterminated conditional directive (missing #endif)");

        _output.Add(new Token(TokenKind.EndOfFile, string.Empty, new SourceLocation(fileName, 0, 0, source.Length), true, false));
        return _output;
    }

    // --- active-region bookkeeping ----------------------------------------

    private bool Active => _conditionals.Count == 0 || _conditionals.Peek().Active;

    // --- main walk --------------------------------------------------------

    private void Run(IReadOnlyList<Token> tokens, string currentDir, int includeDepth)
    {
        var textRun = new List<PpToken>();

        void Flush()
        {
            if (textRun.Count == 0) return;
            foreach (var pp in Expand(new List<PpToken>(textRun)))
                if (!pp.IsPlacemarker)
                    _output.Add(pp.Token);
            textRun.Clear();
        }

        int i = 0;
        while (i < tokens.Count && tokens[i].Kind != TokenKind.EndOfFile)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.Hash && t.AtStartOfLine)
            {
                Flush();
                i = ProcessDirective(tokens, i, currentDir, includeDepth);
                continue;
            }

            if (Active)
                textRun.Add(PpToken.From(t));
            i++;
        }

        Flush();
    }

    /// <summary>Processes one directive line starting at the '#' token; returns the index of the next line.</summary>
    private int ProcessDirective(IReadOnlyList<Token> tokens, int hashIndex, string currentDir, int includeDepth)
    {
        int lineEnd = hashIndex + 1;
        while (lineEnd < tokens.Count && tokens[lineEnd].Kind != TokenKind.EndOfFile && !tokens[lineEnd].AtStartOfLine)
            lineEnd++;

        // Null directive: '#' alone on a line.
        if (hashIndex + 1 >= lineEnd)
            return lineEnd;

        var nameTok = tokens[hashIndex + 1];
        string name = nameTok.Text;
        var rest = Slice(tokens, hashIndex + 2, lineEnd);

        switch (name)
        {
            case "if": PushConditional(ParentActive() && EvaluateIf(rest, nameTok.Location)); break;
            case "ifdef": PushConditional(ParentActive() && IsDefinedOperand(rest, nameTok.Location, "#ifdef")); break;
            case "ifndef": PushConditional(ParentActive() && !IsDefinedOperand(rest, nameTok.Location, "#ifndef")); break;
            case "elif": DoElif(rest, nameTok.Location); break;
            case "else": DoElse(nameTok.Location); break;
            case "endif": DoEndif(nameTok.Location); break;

            case "define": if (Active) DoDefine(rest, nameTok.Location); break;
            case "undef": if (Active) DoUndef(rest, nameTok.Location); break;
            case "include": if (Active) DoInclude(rest, nameTok.Location, currentDir, includeDepth); break;
            case "error": if (Active) Diagnostics.Error(nameTok.Location, "#error " + Spell(rest)); break;
            case "line": /* accepted; line-mapping not yet applied */ break;
            case "pragma": /* implementation-defined: ignored */ break;

            default:
                if (Active)
                    Diagnostics.Error(nameTok.Location, $"invalid preprocessing directive #{name}");
                break;
        }

        return lineEnd;
    }

    // --- conditional directives -------------------------------------------

    private bool ParentActive() => Active;

    private void PushConditional(bool active)
    {
        bool parentActive = Active;
        _conditionals.Push(new CondState
        {
            ParentActive = parentActive,
            Active = active,
            BranchTaken = active,
        });
    }

    private void DoElif(IReadOnlyList<Token> rest, SourceLocation loc)
    {
        if (_conditionals.Count == 0) { Diagnostics.Error(loc, "#elif without #if"); return; }
        var top = _conditionals.Peek();
        if (top.SeenElse) { Diagnostics.Error(loc, "#elif after #else"); return; }

        if (top.ParentActive && !top.BranchTaken)
        {
            bool cond = EvaluateIf(rest, loc);
            top.Active = cond;
            top.BranchTaken = cond;
        }
        else
        {
            top.Active = false;
        }
    }

    private void DoElse(SourceLocation loc)
    {
        if (_conditionals.Count == 0) { Diagnostics.Error(loc, "#else without #if"); return; }
        var top = _conditionals.Peek();
        if (top.SeenElse) { Diagnostics.Error(loc, "#else after #else"); return; }
        top.SeenElse = true;
        top.Active = top.ParentActive && !top.BranchTaken;
        top.BranchTaken = true;
    }

    private void DoEndif(SourceLocation loc)
    {
        if (_conditionals.Count == 0) { Diagnostics.Error(loc, "#endif without #if"); return; }
        _conditionals.Pop();
    }

    private bool IsDefinedOperand(IReadOnlyList<Token> rest, SourceLocation loc, string directive)
    {
        if (rest.Count == 0 || !PpFacts.IsIdentifierLike(rest[0]))
        {
            Diagnostics.Error(loc, $"{directive} expects an identifier");
            return false;
        }
        return _macros.Contains(rest[0].Text);
    }

    private bool EvaluateIf(IReadOnlyList<Token> rest, SourceLocation loc)
    {
        var withDefined = ResolveDefined(rest);
        var expanded = Expand(withDefined.Select(PpToken.From).ToList())
            .Where(pp => !pp.IsPlacemarker)
            .Select(pp => pp.Token)
            .ToList();
        return PpConditionEvaluator.Evaluate(expanded, Diagnostics, loc);
    }

    /// <summary>Replaces <c>defined X</c> / <c>defined(X)</c> with 1 or 0 before macro expansion.</summary>
    private List<Token> ResolveDefined(IReadOnlyList<Token> tokens)
    {
        var result = new List<Token>();
        int i = 0;
        while (i < tokens.Count)
        {
            var t = tokens[i];
            if (PpFacts.IsIdentifierLike(t) && t.Text == "defined")
            {
                if (i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.LParen
                    && i + 2 < tokens.Count && PpFacts.IsIdentifierLike(tokens[i + 2])
                    && i + 3 < tokens.Count && tokens[i + 3].Kind == TokenKind.RParen)
                {
                    result.Add(IntToken(_macros.Contains(tokens[i + 2].Text) ? 1 : 0, t.Location));
                    i += 4;
                    continue;
                }
                if (i + 1 < tokens.Count && PpFacts.IsIdentifierLike(tokens[i + 1]))
                {
                    result.Add(IntToken(_macros.Contains(tokens[i + 1].Text) ? 1 : 0, t.Location));
                    i += 2;
                    continue;
                }
                Diagnostics.Error(t.Location, "operator \"defined\" requires an identifier");
                i++;
                continue;
            }
            result.Add(t);
            i++;
        }
        return result;
    }

    // --- #define / #undef -------------------------------------------------

    private void DoDefine(IReadOnlyList<Token> rest, SourceLocation loc)
    {
        if (rest.Count == 0 || !PpFacts.IsIdentifierLike(rest[0]))
        {
            Diagnostics.Error(loc, "macro name missing or not an identifier");
            return;
        }

        var nameTok = rest[0];
        string macroName = nameTok.Text;
        Macro macro;

        // Function-like requires '(' with NO intervening whitespace; "FOO (x)" is object-like.
        if (rest.Count > 1 && rest[1].Kind == TokenKind.LParen && !rest[1].HasLeadingTrivia)
        {
            if (!TryParseParameters(rest, loc, out var parameters, out int bodyStart))
                return;
            macro = new Macro(macroName, isFunctionLike: true, parameters, Slice(rest, bodyStart, rest.Count), nameTok.Location);
        }
        else
        {
            macro = new Macro(macroName, isFunctionLike: false, [], Slice(rest, 1, rest.Count), nameTok.Location);
        }

        var previous = _macros.Define(macro);
        if (previous != null && !previous.IsIdenticalTo(macro))
            Diagnostics.Warning(loc, $"'{macroName}' redefined");
    }

    private bool TryParseParameters(IReadOnlyList<Token> rest, SourceLocation loc, out List<string> parameters, out int bodyStart)
    {
        parameters = [];
        bodyStart = rest.Count;

        int i = 2; // past name and '('
        bool expectParam = true;
        while (i < rest.Count)
        {
            var t = rest[i];
            if (t.Kind == TokenKind.RParen)
            {
                bodyStart = i + 1;
                return true;
            }
            if (t.Kind == TokenKind.Ellipsis)
            {
                Diagnostics.Error(t.Location, "variadic macros are not allowed in C89");
                return false;
            }
            if (expectParam)
            {
                if (!PpFacts.IsIdentifierLike(t))
                {
                    Diagnostics.Error(t.Location, "expected parameter name in macro definition");
                    return false;
                }
                if (parameters.Contains(t.Text))
                {
                    Diagnostics.Error(t.Location, $"duplicate macro parameter '{t.Text}'");
                    return false;
                }
                parameters.Add(t.Text);
                expectParam = false;
            }
            else
            {
                if (t.Kind != TokenKind.Comma)
                {
                    Diagnostics.Error(t.Location, "expected ',' or ')' in macro parameter list");
                    return false;
                }
                expectParam = true;
            }
            i++;
        }

        Diagnostics.Error(loc, "missing ')' in macro parameter list");
        return false;
    }

    private void DoUndef(IReadOnlyList<Token> rest, SourceLocation loc)
    {
        if (rest.Count == 0 || !PpFacts.IsIdentifierLike(rest[0]))
        {
            Diagnostics.Error(loc, "macro name missing or not an identifier");
            return;
        }
        _macros.Undef(rest[0].Text);
    }

    // --- #include ---------------------------------------------------------

    private void DoInclude(IReadOnlyList<Token> rest, SourceLocation loc, string currentDir, int includeDepth)
    {
        if (!TryReadHeaderName(rest, loc, out string path, out bool angled))
            return;

        if (includeDepth >= _options.MaxIncludeDepth)
        {
            Diagnostics.Error(loc, $"#include nested too deeply (> {_options.MaxIncludeDepth})");
            return;
        }

        if (!_resolver.TryResolve(path, angled, currentDir, out var resolved))
        {
            Diagnostics.Error(loc, $"{(angled ? '<' + path + '>' : '"' + path + '"')}: No such file or directory");
            return;
        }

        var tokens = new Lexer(resolved.Content, resolved.FullPath, Diagnostics, _options.LexerOptions).Tokenize();
        Run(tokens, DirectoryOf(resolved.FullPath), includeDepth + 1);
    }

    private bool TryReadHeaderName(IReadOnlyList<Token> rest, SourceLocation loc, out string path, out bool angled)
    {
        path = string.Empty;
        angled = false;

        // Computed include: a macro expands to the header name.
        if (rest.Count > 0 && rest[0].Kind != TokenKind.StringLiteral && rest[0].Kind != TokenKind.Less)
        {
            rest = Expand(rest.Select(PpToken.From).ToList())
                .Where(pp => !pp.IsPlacemarker)
                .Select(pp => pp.Token)
                .ToList();
        }

        if (rest.Count == 0)
        {
            Diagnostics.Error(loc, "#include expects \"FILENAME\" or <FILENAME>");
            return false;
        }

        if (rest[0].Kind == TokenKind.StringLiteral)
        {
            string text = rest[0].Text;
            path = text.Length >= 2 ? text[1..^1] : string.Empty;
            angled = false;
            return true;
        }

        if (rest[0].Kind == TokenKind.Less)
        {
            angled = true;
            var sb = new StringBuilder();
            int i = 1;
            for (; i < rest.Count && rest[i].Kind != TokenKind.Greater; i++)
            {
                if (sb.Length > 0 && rest[i].HasLeadingTrivia) sb.Append(' ');
                sb.Append(rest[i].Text);
            }
            if (i >= rest.Count)
            {
                Diagnostics.Error(loc, "missing terminating '>' in #include");
                return false;
            }
            path = sb.ToString();
            return true;
        }

        Diagnostics.Error(loc, "#include expects \"FILENAME\" or <FILENAME>");
        return false;
    }

    // --- macro expansion (Prosser) ----------------------------------------

    private List<PpToken> Expand(List<PpToken> input)
    {
        var output = new List<PpToken>();
        int pos = 0;

        while (pos < input.Count)
        {
            if (output.Count > _options.MaxExpansionTokens)
            {
                Diagnostics.Error(input[pos].Token.Location, "macro expansion produced too many tokens (possible runaway)");
                break;
            }

            var t = input[pos];

            if (t.Kind == TokenKind.Identifier || t.Token.IsKeyword)
            {
                if (TryExpandDynamic(t, out var dyn))
                {
                    output.Add(dyn);
                    pos++;
                    continue;
                }

                if (!t.HideSet.Contains(t.Text) && _macros.TryGet(t.Text) is { } macro)
                {
                    if (!macro.IsFunctionLike)
                    {
                        var hs = t.HideSet.Add(macro.Name);
                        var repl = Subst(macro, args: null, hs);
                        input.RemoveRange(pos, 1);
                        input.InsertRange(pos, repl);
                        continue;
                    }

                    // Function-like: expand only when actually invoked with '('.
                    if (pos + 1 < input.Count && input[pos + 1].Kind == TokenKind.LParen)
                    {
                        var args = CollectArguments(input, pos + 1, macro, out var closeParen, out int endExclusive);
                        if (args != null)
                        {
                            var hs = t.HideSet.Intersect(closeParen.HideSet).Add(macro.Name);
                            var repl = Subst(macro, args, hs);
                            input.RemoveRange(pos, endExclusive - pos);
                            input.InsertRange(pos, repl);
                            continue;
                        }
                    }
                }
            }

            output.Add(t);
            pos++;
        }

        return output;
    }

    /// <summary>Collects the argument lists of a function-like invocation. Returns null on error.</summary>
    private List<List<PpToken>>? CollectArguments(List<PpToken> input, int openParenIndex, Macro macro, out PpToken closeParen, out int endExclusive)
    {
        closeParen = default;
        endExclusive = openParenIndex + 1;

        var args = new List<List<PpToken>>();
        var current = new List<PpToken>();
        int depth = 1;
        int i = openParenIndex + 1;

        for (; i < input.Count; i++)
        {
            var tk = input[i];
            if (tk.Kind == TokenKind.LParen) { depth++; current.Add(tk); }
            else if (tk.Kind == TokenKind.RParen)
            {
                depth--;
                if (depth == 0) { closeParen = tk; i++; break; }
                current.Add(tk);
            }
            else if (tk.Kind == TokenKind.Comma && depth == 1)
            {
                args.Add(current);
                current = [];
            }
            else current.Add(tk);
        }

        if (depth != 0)
        {
            Diagnostics.Error(input[openParenIndex].Token.Location, $"unterminated argument list invoking macro '{macro.Name}'");
            return null;
        }

        endExclusive = i;

        // Distinguish "no arguments" (FOO()) from "one empty argument".
        if (args.Count == 0 && current.Count == 0)
        {
            if (macro.Parameters.Count == 0) return args; // genuinely zero args
            if (macro.Parameters.Count == 1) { args.Add(current); return args; } // one empty arg
        }
        else
        {
            args.Add(current);
        }

        if (args.Count != macro.Parameters.Count)
        {
            Diagnostics.Error(input[openParenIndex].Token.Location,
                $"macro '{macro.Name}' passed {args.Count} argument(s) but takes {macro.Parameters.Count}");
            // Pad/truncate so expansion can continue.
            while (args.Count < macro.Parameters.Count) args.Add([]);
            if (args.Count > macro.Parameters.Count) args.RemoveRange(macro.Parameters.Count, args.Count - macro.Parameters.Count);
        }

        return args;
    }

    /// <summary>Substitutes a macro body: parameter replacement, <c>#</c> stringize, <c>##</c> paste.</summary>
    private List<PpToken> Subst(Macro macro, List<List<PpToken>>? args, ImmutableHashSet<string> hideSet)
    {
        var paramIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int p = 0; p < macro.Parameters.Count; p++)
            paramIndex[macro.Parameters[p]] = p;

        var rawArgs = args;
        var expandedArgs = new List<PpToken>?[macro.Parameters.Count];

        List<PpToken> ExpandedArg(int idx)
            => expandedArgs[idx] ??= Expand(new List<PpToken>(rawArgs![idx]));

        var body = macro.Replacement;
        var result = new List<PpToken>();
        int i = 0;

        while (i < body.Count)
        {
            var cur = body[i];

            // '#' stringize (function-like only).
            if (macro.IsFunctionLike && cur.Kind == TokenKind.Hash
                && i + 1 < body.Count && IsParamToken(body[i + 1], paramIndex, out int sIdx))
            {
                result.Add(Stringize(rawArgs![sIdx], cur.Location));
                i += 2;
                continue;
            }

            // '##' paste.
            if (cur.Kind == TokenKind.HashHash && i + 1 < body.Count)
            {
                var rhs = body[i + 1];
                IReadOnlyList<PpToken> rhsToks = IsParamToken(rhs, paramIndex, out int rIdx)
                    ? rawArgs![rIdx]
                    : [PpToken.From(rhs)];
                GlueOnto(result, rhsToks);
                i += 2;
                continue;
            }

            // Operand immediately before '##' is used unexpanded.
            if (i + 1 < body.Count && body[i + 1].Kind == TokenKind.HashHash)
            {
                if (IsParamToken(cur, paramIndex, out int lIdx))
                {
                    var raw = rawArgs![lIdx];
                    result.Add(raw.Count == 0 ? PpToken.Placemarker() : raw[0]);
                    for (int k = 1; k < raw.Count; k++) result.Add(raw[k]);
                }
                else result.Add(PpToken.From(cur));
                i++;
                continue;
            }

            // Ordinary parameter: substitute its fully expanded argument.
            if (IsParamToken(cur, paramIndex, out int xIdx))
            {
                result.AddRange(ExpandedArg(xIdx));
                i++;
                continue;
            }

            result.Add(PpToken.From(cur));
            i++;
        }

        // Apply the hide set to the whole result and drop placemarkers.
        return result
            .Where(pp => !pp.IsPlacemarker)
            .Select(pp => pp.WithHideSet(pp.HideSet.Union(hideSet)))
            .ToList();
    }

    private static bool IsParamToken(in Token tok, Dictionary<string, int> paramIndex, out int idx)
    {
        idx = -1;
        return (tok.Kind == TokenKind.Identifier || tok.IsKeyword) && paramIndex.TryGetValue(tok.Text, out idx);
    }

    private void GlueOnto(List<PpToken> result, IReadOnlyList<PpToken> rhsToks)
    {
        PpToken lhs = result.Count > 0 ? result[^1] : PpToken.Placemarker();
        if (result.Count > 0) result.RemoveAt(result.Count - 1);

        PpToken first = rhsToks.Count > 0 ? rhsToks[0] : PpToken.Placemarker();

        if (lhs.IsPlacemarker)
        {
            result.Add(first);
        }
        else if (first.IsPlacemarker)
        {
            result.Add(lhs);
        }
        else
        {
            var relexed = ReLex(lhs.Text + first.Text);
            if (relexed.Count != 1)
                Diagnostics.Error(lhs.Token.Location, $"pasting \"{lhs.Text}\" and \"{first.Text}\" does not form a valid token");
            foreach (var r in relexed)
                result.Add(PpToken.From(r with { Location = lhs.Token.Location }));
        }

        for (int k = 1; k < rhsToks.Count; k++)
            result.Add(rhsToks[k]);
    }

    private PpToken Stringize(IReadOnlyList<PpToken> argTokens, SourceLocation loc)
    {
        var sb = new StringBuilder();
        sb.Append('"');
        for (int k = 0; k < argTokens.Count; k++)
        {
            var tok = argTokens[k];
            if (k > 0 && tok.Token.HasLeadingTrivia) sb.Append(' ');

            if (tok.Kind == TokenKind.StringLiteral || tok.Kind == TokenKind.CharacterConstant)
            {
                foreach (char c in tok.Text)
                {
                    if (c is '\\' or '"') sb.Append('\\');
                    sb.Append(c);
                }
            }
            else
            {
                sb.Append(tok.Text);
            }
        }
        sb.Append('"');
        return PpToken.From(new Token(TokenKind.StringLiteral, sb.ToString(), loc, false, false));
    }

    // --- predefined / dynamic macros --------------------------------------

    private void PredefineMacros()
    {
        if (_options.DefineStdc) DefinePredefined("__STDC__", "1");
        DefinePredefined("__DATE__", _dateMacro);
        DefinePredefined("__TIME__", _timeMacro);

        foreach (var (key, value) in _options.PredefinedMacros)
            DefinePredefined(key, string.IsNullOrEmpty(value) ? "1" : value);
    }

    private void DefinePredefined(string name, string body)
    {
        var tokens = new Lexer(body, "<command-line>", new DiagnosticBag(), _options.LexerOptions)
            .Tokenize()
            .Where(t => t.Kind != TokenKind.EndOfFile)
            .ToList();
        _macros.Define(new Macro(name, isFunctionLike: false, [], tokens, new SourceLocation("<command-line>", 0, 0, 0)));
    }

    /// <summary>Handles <c>__LINE__</c> / <c>__FILE__</c>, whose values depend on the use site.</summary>
    private bool TryExpandDynamic(in PpToken t, out PpToken expanded)
    {
        switch (t.Text)
        {
            case "__LINE__":
                expanded = PpToken.From(new Token(TokenKind.IntegerConstant,
                    t.Token.Location.Line.ToString(CultureInfo.InvariantCulture), t.Token.Location, false, t.Token.HasLeadingTrivia));
                return true;
            case "__FILE__":
                expanded = PpToken.From(new Token(TokenKind.StringLiteral,
                    $"\"{EscapeForString(t.Token.Location.FileName)}\"", t.Token.Location, false, t.Token.HasLeadingTrivia));
                return true;
            default:
                expanded = default;
                return false;
        }
    }

    // --- helpers ----------------------------------------------------------

    private static string EscapeForString(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c is '\\' or '"') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    private List<Token> ReLex(string text)
        => new Lexer(text, "<paste>", new DiagnosticBag(), _options.LexerOptions)
            .Tokenize()
            .Where(t => t.Kind != TokenKind.EndOfFile)
            .ToList();

    private static Token IntToken(long value, SourceLocation loc)
        => new(TokenKind.IntegerConstant, value.ToString(CultureInfo.InvariantCulture), loc, false, true);

    private static List<Token> Slice(IReadOnlyList<Token> tokens, int start, int endExclusive)
    {
        var list = new List<Token>(Math.Max(0, endExclusive - start));
        for (int i = start; i < endExclusive; i++) list.Add(tokens[i]);
        return list;
    }

    private static string Spell(IReadOnlyList<Token> tokens)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (i > 0 && tokens[i].HasLeadingTrivia) sb.Append(' ');
            sb.Append(tokens[i].Text);
        }
        return sb.ToString();
    }

    private static string DirectoryOf(string fileName)
    {
        try { return Path.GetDirectoryName(Path.GetFullPath(fileName)) ?? string.Empty; }
        catch (ArgumentException) { return string.Empty; }
    }
}
