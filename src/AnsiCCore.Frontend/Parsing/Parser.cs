using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;

namespace AnsiCCore.Frontend.Parsing;

/// <summary>
/// Hand-written recursive-descent parser for ANSI C (C89), consuming a preprocessed token
/// stream and producing a <see cref="TranslationUnit"/> parse tree.
/// </summary>
/// <remarks>
/// C's grammar is context-sensitive: <c>A * B;</c> is a declaration when <c>A</c> is a
/// <c>typedef</c>-name and a multiplication otherwise. The parser therefore tracks declared
/// <c>typedef</c>-names in a lexical scope stack (<see cref="_scopes"/>) and consults it while
/// deciding whether a construct is a declaration, a type-name, or an expression. Errors are
/// reported to the <see cref="DiagnosticBag"/> and recovered from by panic-mode synchronization
/// so a single run can surface many problems.
/// </remarks>
public sealed partial class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _pos;

    // Lexical scopes: name -> isTypedefName. Innermost scope is last.
    private readonly List<Dictionary<string, bool>> _scopes = [new(StringComparer.Ordinal)];

    /// <summary>Thrown internally to unwind to the nearest synchronization point.</summary>
    private sealed class ParseError : Exception;

    public Parser(IReadOnlyList<Token> tokens, DiagnosticBag? diagnostics = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        Diagnostics = diagnostics ?? new DiagnosticBag();
    }

    public DiagnosticBag Diagnostics { get; }

    /// <summary>Parses the whole translation unit. Never throws for malformed input — it recovers.</summary>
    public TranslationUnit ParseTranslationUnit()
    {
        var start = Current.Location;
        var items = new List<Node>();

        while (!AtEnd)
        {
            try
            {
                items.Add(ParseExternalDeclaration());
            }
            catch (ParseError)
            {
                SynchronizeTopLevel();
            }
        }

        return new TranslationUnit(start, items);
    }

    // --- token cursor -----------------------------------------------------

    private Token Current => _tokens[Math.Min(_pos, _tokens.Count - 1)];
    private bool AtEnd => Current.Kind == TokenKind.EndOfFile;

    private Token Peek(int ahead = 1)
    {
        int i = _pos + ahead;
        return i < _tokens.Count ? _tokens[i] : _tokens[^1];
    }

    private TokenKind Kind => Current.Kind;

    private Token Advance()
    {
        var t = Current;
        if (_pos < _tokens.Count - 1) _pos++;
        return t;
    }

    private bool Check(TokenKind kind) => Kind == kind;

    private bool Match(TokenKind kind)
    {
        if (Check(kind)) { Advance(); return true; }
        return false;
    }

    private Token Expect(TokenKind kind, string what)
    {
        if (Check(kind)) return Advance();
        throw Error($"expected {what}");
    }

    private ParseError Error(string message)
    {
        Diagnostics.Error(Current.Location, AtEnd ? $"{message} at end of file" : $"{message} before '{Current.Text}'");
        return new ParseError();
    }

    // --- recovery ---------------------------------------------------------

    /// <summary>After a top-level error, skip to the next ';' or '}' (consuming it) or a declaration start.</summary>
    private void SynchronizeTopLevel()
    {
        while (!AtEnd)
        {
            if (Match(TokenKind.Semicolon)) return;
            if (Check(TokenKind.RBrace)) { Advance(); return; }
            Advance();
        }
    }

    /// <summary>Inside a block, skip to the next ';' or '}' to resume at a statement boundary.</summary>
    private void SynchronizeStatement()
    {
        while (!AtEnd)
        {
            if (Match(TokenKind.Semicolon)) return;
            if (Check(TokenKind.RBrace)) return;
            Advance();
        }
    }

    // --- scope / typedef tracking -----------------------------------------

    private void PushScope() => _scopes.Add(new Dictionary<string, bool>(StringComparer.Ordinal));

    private void PopScope()
    {
        if (_scopes.Count > 1) _scopes.RemoveAt(_scopes.Count - 1);
    }

    private void DeclareName(string name, bool isTypedef)
        => _scopes[^1][name] = isTypedef;

    private bool IsTypedefName(string name)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
            if (_scopes[i].TryGetValue(name, out bool isTypedef))
                return isTypedef; // nearest declaration wins (allows shadowing a typedef with an object)
        return false;
    }

    // --- shared classification helpers ------------------------------------

    private static bool IsStorageClassKeyword(TokenKind k) => k is
        TokenKind.KwTypedef or TokenKind.KwExtern or TokenKind.KwStatic or TokenKind.KwAuto or TokenKind.KwRegister;

    private static bool IsTypeQualifierKeyword(TokenKind k) => k is TokenKind.KwConst or TokenKind.KwVolatile;

    private static bool IsPrimitiveTypeKeyword(TokenKind k) => k is
        TokenKind.KwVoid or TokenKind.KwChar or TokenKind.KwShort or TokenKind.KwInt or TokenKind.KwLong
        or TokenKind.KwFloat or TokenKind.KwDouble or TokenKind.KwSigned or TokenKind.KwUnsigned;

    private static bool IsTypeAggregateKeyword(TokenKind k) => k is
        TokenKind.KwStruct or TokenKind.KwUnion or TokenKind.KwEnum;

    /// <summary>True if <paramref name="t"/> can begin a declaration specifier (incl. a typedef-name).</summary>
    private bool IsDeclarationSpecifierStart(in Token t)
    {
        if (IsStorageClassKeyword(t.Kind) || IsTypeQualifierKeyword(t.Kind)
            || IsPrimitiveTypeKeyword(t.Kind) || IsTypeAggregateKeyword(t.Kind))
            return true;
        return t.Kind == TokenKind.Identifier && IsTypedefName(t.Text);
    }

    /// <summary>True if the current position begins a declaration (used for block items and for-clauses).</summary>
    private bool IsDeclarationStart() => IsDeclarationSpecifierStart(Current);

    /// <summary>True if the current position begins a type-name (used for casts and sizeof).</summary>
    private bool IsTypeNameStart() => IsDeclarationSpecifierStart(Current);
}
