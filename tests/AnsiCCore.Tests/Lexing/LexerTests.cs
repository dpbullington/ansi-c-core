using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;

namespace AnsiCCore.Tests.Lexing;

[TestFixture]
public class LexerTests
{
    private static IReadOnlyList<Token> Lex(string src, out DiagnosticBag diags, LexerOptions? options = null)
    {
        var lexer = new Lexer(src, "test.c", options: options);
        var tokens = lexer.Tokenize();
        diags = lexer.Diagnostics;
        return tokens;
    }

    private static IReadOnlyList<Token> Lex(string src) => Lex(src, out _);

    /// <summary>All tokens except the trailing EndOfFile.</summary>
    private static TokenKind[] Kinds(string src)
        => Lex(src).Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Kind).ToArray();

    [Test]
    public void EmptyInput_ProducesOnlyEof()
    {
        var tokens = Lex("");
        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Kind, Is.EqualTo(TokenKind.EndOfFile));
    }

    [Test]
    public void WhitespaceOnly_ProducesOnlyEof()
    {
        var tokens = Lex("   \t\n  \r\n ");
        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Kind, Is.EqualTo(TokenKind.EndOfFile));
    }

    [Test]
    public void Keywords_AreRecognized()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Lex("int")[0].Kind, Is.EqualTo(TokenKind.KwInt));
            Assert.That(Lex("return")[0].Kind, Is.EqualTo(TokenKind.KwReturn));
            Assert.That(Lex("sizeof")[0].Kind, Is.EqualTo(TokenKind.KwSizeof));
            Assert.That(Lex("unsigned")[0].Kind, Is.EqualTo(TokenKind.KwUnsigned));
        });
    }

    [Test]
    public void AllThirtyTwoKeywords_MapToKeywordKinds()
    {
        string[] kws =
        [
            "auto","break","case","char","const","continue","default","do","double","else",
            "enum","extern","float","for","goto","if","int","long","register","return","short",
            "signed","sizeof","static","struct","switch","typedef","union","unsigned","void",
            "volatile","while",
        ];
        Assert.That(kws, Has.Length.EqualTo(32));
        foreach (var kw in kws)
        {
            var tok = Lex(kw)[0];
            Assert.That(tok.IsKeyword, Is.True, $"'{kw}' should be a keyword, got {tok.Kind}");
        }
    }

    [Test]
    public void Identifiers_AreNotKeywords()
    {
        foreach (var id in new[] { "intx", "x", "_foo", "foo_bar99", "Integer", "L" /* bare L */ })
        {
            var tok = Lex(id)[0];
            Assert.That(tok.Kind, Is.EqualTo(TokenKind.Identifier), $"'{id}'");
            Assert.That(tok.Text, Is.EqualTo(id));
        }
    }

    [Test]
    public void IntegerConstants_AreClassified()
    {
        foreach (var n in new[] { "0", "42", "0x1F", "0XabcUL", "010", "100u", "7L", "123ull" })
        {
            var tokens = Lex(n, out var diags);
            Assert.That(diags.HasErrors, Is.False, $"'{n}' diagnostics");
            Assert.That(tokens[0].Kind, Is.EqualTo(TokenKind.IntegerConstant), $"'{n}'");
            Assert.That(tokens[0].Text, Is.EqualTo(n));
        }
    }

    [Test]
    public void FloatingConstants_AreClassified()
    {
        foreach (var n in new[] { "1.0", ".5", "3.", "1e10", "1.5e-3", "2E+4f", "0.0L", "6.022e23" })
        {
            var tokens = Lex(n, out var diags);
            Assert.That(diags.HasErrors, Is.False, $"'{n}' diagnostics");
            Assert.That(tokens[0].Kind, Is.EqualTo(TokenKind.FloatingConstant), $"'{n}'");
            Assert.That(tokens[0].Text, Is.EqualTo(n));
        }
    }

    [Test]
    public void DotFollowedByNonDigit_IsMemberAccess_NotNumber()
    {
        Assert.That(Kinds("a.b"), Is.EqualTo(new[] { TokenKind.Identifier, TokenKind.Dot, TokenKind.Identifier }));
    }

    [Test]
    public void InvalidSuffixOnNumber_ReportsError()
    {
        Lex("123abc", out var diags);
        Assert.That(diags.HasErrors, Is.True);
        Assert.That(diags.Single().Message, Does.Contain("invalid suffix"));
    }

    [Test]
    public void ExponentWithoutDigits_ReportsError()
    {
        Lex("1e", out var diags);
        Assert.That(diags.HasErrors, Is.True);
        Assert.That(diags.Single().Message, Does.Contain("exponent has no digits"));
    }

    [Test]
    public void CharacterConstant_IsLexedWholeWithEscapes()
    {
        var tok = Lex(@"'\n'", out var diags)[0];
        Assert.That(diags.HasErrors, Is.False);
        Assert.That(tok.Kind, Is.EqualTo(TokenKind.CharacterConstant));
        Assert.That(tok.Text, Is.EqualTo(@"'\n'"));
    }

    [Test]
    public void EscapedQuoteInsideCharConstant_DoesNotTerminateEarly()
    {
        var tok = Lex(@"'\''", out var diags)[0];
        Assert.That(diags.HasErrors, Is.False);
        Assert.That(tok.Text, Is.EqualTo(@"'\''"));
    }

    [Test]
    public void EmptyCharacterConstant_ReportsError()
    {
        Lex("''", out var diags);
        Assert.That(diags.HasErrors, Is.True);
        Assert.That(diags.Single().Message, Does.Contain("empty character constant"));
    }

    [Test]
    public void StringLiteral_HandlesEscapedQuote()
    {
        var tok = Lex("\"a\\\"b\"", out var diags)[0]; // "a\"b"
        Assert.That(diags.HasErrors, Is.False);
        Assert.That(tok.Kind, Is.EqualTo(TokenKind.StringLiteral));
    }

    [Test]
    public void WideLiterals_ArePrefixedCorrectly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Lex("L\"wide\"")[0].Kind, Is.EqualTo(TokenKind.StringLiteral));
            Assert.That(Lex("L'x'")[0].Kind, Is.EqualTo(TokenKind.CharacterConstant));
            // Glued prefix only: "Lx" is an identifier, not a wide anything.
            Assert.That(Lex("Lx")[0].Kind, Is.EqualTo(TokenKind.Identifier));
        });
    }

    [Test]
    public void UnterminatedString_ReportsError()
    {
        Lex("\"no end\n", out var diags);
        Assert.That(diags.HasErrors, Is.True);
        Assert.That(diags.Single().Message, Does.Contain("unterminated string"));
    }

    [Test]
    public void UnterminatedBlockComment_ReportsError()
    {
        Lex("a /* unterminated", out var diags);
        Assert.That(diags.HasErrors, Is.True);
        Assert.That(diags.Single().Message, Does.Contain("unterminated block comment"));
    }

    [Test]
    public void BlockComment_IsTreatedAsWhitespace()
    {
        Assert.That(Kinds("a/* comment */b"), Is.EqualTo(new[] { TokenKind.Identifier, TokenKind.Identifier }));
    }

    [Test]
    public void LineComments_RejectedByDefault_AcceptedWhenEnabled()
    {
        // Strict C89: "//" is two slash tokens.
        Assert.That(Kinds("a//b"), Is.EqualTo(new[] { TokenKind.Identifier, TokenKind.Slash, TokenKind.Slash, TokenKind.Identifier }));

        // Relaxed dialect: consumed as a comment.
        var relaxed = Lex("a//b\nc", out _, new LexerOptions { AllowLineComments = true })
            .Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Kind).ToArray();
        Assert.That(relaxed, Is.EqualTo(new[] { TokenKind.Identifier, TokenKind.Identifier }));
    }

    [Test]
    public void MaximalMunch_PrefersLongestOperator()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Kinds(">>="), Is.EqualTo(new[] { TokenKind.GreaterGreaterEqual }));
            Assert.That(Kinds("<<="), Is.EqualTo(new[] { TokenKind.LessLessEqual }));
            Assert.That(Kinds("->"), Is.EqualTo(new[] { TokenKind.Arrow }));
            Assert.That(Kinds("++"), Is.EqualTo(new[] { TokenKind.PlusPlus }));
            Assert.That(Kinds("..."), Is.EqualTo(new[] { TokenKind.Ellipsis }));
            Assert.That(Kinds("=="), Is.EqualTo(new[] { TokenKind.EqualEqual }));
            Assert.That(Kinds("##"), Is.EqualTo(new[] { TokenKind.HashHash }));
        });
    }

    [Test]
    public void AdjacentOperators_SplitCorrectly()
    {
        // a=-b is 'a', '=', '-', 'b' (not 'a', '-=', 'b') under maximal munch left-to-right.
        Assert.That(Kinds("a=-b"), Is.EqualTo(new[]
        {
            TokenKind.Identifier, TokenKind.Equal, TokenKind.Minus, TokenKind.Identifier,
        }));

        // ..x is '.', '.', 'x' (only three dots form an ellipsis).
        Assert.That(Kinds("..x"), Is.EqualTo(new[] { TokenKind.Dot, TokenKind.Dot, TokenKind.Identifier }));
    }

    [Test]
    public void StrayCharacter_ReportsErrorAndEmitsUnknown()
    {
        var tokens = Lex("@", out var diags);
        Assert.That(diags.HasErrors, Is.True);
        Assert.That(tokens[0].Kind, Is.EqualTo(TokenKind.Unknown));
    }

    [Test]
    public void LocationTracking_IsLineAndColumnAccurate()
    {
        // line 1:   int x;
        // line 2:     y
        var tokens = Lex("int x;\n  y");
        Assert.Multiple(() =>
        {
            Assert.That(tokens[0].Location.Line, Is.EqualTo(1));
            Assert.That(tokens[0].Location.Column, Is.EqualTo(1));   // int
            Assert.That(tokens[1].Location.Column, Is.EqualTo(5));   // x
            Assert.That(tokens[2].Location.Column, Is.EqualTo(6));   // ;
            Assert.That(tokens[3].Location.Line, Is.EqualTo(2));     // y
            Assert.That(tokens[3].Location.Column, Is.EqualTo(3));
        });
    }

    [Test]
    public void TriviaFlags_TrackStartOfLineAndLeadingSpace()
    {
        var tokens = Lex("a b\nc");
        Assert.Multiple(() =>
        {
            Assert.That(tokens[0].AtStartOfLine, Is.True);              // a
            Assert.That(tokens[0].HasLeadingTrivia, Is.False);
            Assert.That(tokens[1].AtStartOfLine, Is.False);            // b
            Assert.That(tokens[1].HasLeadingTrivia, Is.True);
            Assert.That(tokens[2].AtStartOfLine, Is.True);             // c
        });
    }

    [Test]
    public void LineContinuation_IsSplicedAsWhitespace()
    {
        var tokens = Lex("a\\\nb");
        Assert.Multiple(() =>
        {
            Assert.That(tokens[0].Kind, Is.EqualTo(TokenKind.Identifier));
            Assert.That(tokens[1].Kind, Is.EqualTo(TokenKind.Identifier));
            // The continuation means 'b' is NOT treated as the start of a new logical line.
            Assert.That(tokens[1].AtStartOfLine, Is.False);
        });
    }

    [Test]
    public void SmallProgram_TokenStreamIsCorrect()
    {
        const string src = "int main(void) { return 0; }";
        Assert.That(Kinds(src), Is.EqualTo(new[]
        {
            TokenKind.KwInt, TokenKind.Identifier, TokenKind.LParen, TokenKind.KwVoid, TokenKind.RParen,
            TokenKind.LBrace, TokenKind.KwReturn, TokenKind.IntegerConstant, TokenKind.Semicolon, TokenKind.RBrace,
        }));
    }
}
