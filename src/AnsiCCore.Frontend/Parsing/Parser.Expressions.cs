using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;

namespace AnsiCCore.Frontend.Parsing;

public sealed partial class Parser
{
    // expression := assignment ( ',' assignment )*
    private Expression ParseExpression()
    {
        var expr = ParseAssignment();
        while (Check(TokenKind.Comma))
        {
            var loc = Advance().Location;
            var right = ParseAssignment();
            expr = new CommaExpression(loc, expr, right);
        }
        return expr;
    }

    /// <summary>Constant expressions are conditional expressions (no comma, no assignment).</summary>
    private Expression ParseConstantExpression() => ParseConditional();

    // assignment := conditional ( assignment-op assignment )?
    private Expression ParseAssignment()
    {
        var left = ParseConditional();
        if (IsAssignmentOperator(Kind))
        {
            var op = Advance();
            var value = ParseAssignment();
            return new AssignmentExpression(op.Location, op.Kind, left, value);
        }
        return left;
    }

    private static bool IsAssignmentOperator(TokenKind k) => k is
        TokenKind.Equal or TokenKind.StarEqual or TokenKind.SlashEqual or TokenKind.PercentEqual
        or TokenKind.PlusEqual or TokenKind.MinusEqual or TokenKind.LessLessEqual or TokenKind.GreaterGreaterEqual
        or TokenKind.AmpEqual or TokenKind.CaretEqual or TokenKind.PipeEqual;

    // conditional := binary ( '?' expression ':' conditional )?
    private Expression ParseConditional()
    {
        var cond = ParseBinary(0);
        if (Check(TokenKind.Question))
        {
            var loc = Advance().Location;
            var whenTrue = ParseExpression();
            Expect(TokenKind.Colon, "':' in conditional expression");
            var whenFalse = ParseConditional();
            return new ConditionalExpression(loc, cond, whenTrue, whenFalse);
        }
        return cond;
    }

    // Precedence-climbing over the binary operators; operands are cast-expressions.
    private Expression ParseBinary(int minPrecedence)
    {
        var left = ParseCast();
        while (true)
        {
            int prec = BinaryPrecedence(Kind);
            if (prec == 0 || prec < minPrecedence) break;
            var op = Advance();
            var right = ParseBinary(prec + 1); // all binary operators are left-associative
            left = new BinaryExpression(op.Location, op.Kind, left, right);
        }
        return left;
    }

    private static int BinaryPrecedence(TokenKind k) => k switch
    {
        TokenKind.Star or TokenKind.Slash or TokenKind.Percent => 11,
        TokenKind.Plus or TokenKind.Minus => 10,
        TokenKind.LessLess or TokenKind.GreaterGreater => 9,
        TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual => 8,
        TokenKind.EqualEqual or TokenKind.BangEqual => 7,
        TokenKind.Ampersand => 6,
        TokenKind.Caret => 5,
        TokenKind.Pipe => 4,
        TokenKind.AmpAmp => 3,
        TokenKind.PipePipe => 2,
        _ => 0,
    };

    // cast := '(' type-name ')' cast | unary
    private Expression ParseCast()
    {
        if (Check(TokenKind.LParen) && IsDeclarationSpecifierStart(Peek()))
        {
            var loc = Advance().Location; // '('
            var type = ParseTypeName();
            Expect(TokenKind.RParen, "')' after type name");
            var operand = ParseCast();
            return new CastExpression(loc, type, operand);
        }
        return ParseUnary();
    }

    // unary := ('++'|'--') unary
    //        | ('&'|'*'|'+'|'-'|'~'|'!') cast
    //        | 'sizeof' unary | 'sizeof' '(' type-name ')'
    //        | postfix
    private Expression ParseUnary()
    {
        switch (Kind)
        {
            case TokenKind.PlusPlus:
            case TokenKind.MinusMinus:
            {
                var op = Advance();
                var operand = ParseUnary();
                return new UnaryExpression(op.Location, op.Kind, operand);
            }
            case TokenKind.Ampersand:
            case TokenKind.Star:
            case TokenKind.Plus:
            case TokenKind.Minus:
            case TokenKind.Tilde:
            case TokenKind.Bang:
            {
                var op = Advance();
                var operand = ParseCast();
                return new UnaryExpression(op.Location, op.Kind, operand);
            }
            case TokenKind.KwSizeof:
            {
                var loc = Advance().Location;
                if (Check(TokenKind.LParen) && IsDeclarationSpecifierStart(Peek()))
                {
                    Advance(); // '('
                    var type = ParseTypeName();
                    Expect(TokenKind.RParen, "')' after type name");
                    return new SizeOfTypeExpression(loc, type);
                }
                return new SizeOfExpression(loc, ParseUnary());
            }
            default:
                return ParsePostfix();
        }
    }

    // postfix := primary ( '[' expr ']' | '(' args? ')' | '.' id | '->' id | '++' | '--' )*
    private Expression ParsePostfix()
    {
        var expr = ParsePrimary();
        while (true)
        {
            switch (Kind)
            {
                case TokenKind.LBracket:
                {
                    var loc = Advance().Location;
                    var index = ParseExpression();
                    Expect(TokenKind.RBracket, "']'");
                    expr = new IndexExpression(loc, expr, index);
                    break;
                }
                case TokenKind.LParen:
                {
                    var loc = Advance().Location;
                    var args = new List<Expression>();
                    if (!Check(TokenKind.RParen))
                    {
                        do { args.Add(ParseAssignment()); }
                        while (Match(TokenKind.Comma));
                    }
                    Expect(TokenKind.RParen, "')' after arguments");
                    expr = new CallExpression(loc, expr, args);
                    break;
                }
                case TokenKind.Dot:
                case TokenKind.Arrow:
                {
                    var op = Advance();
                    var member = Expect(TokenKind.Identifier, "member name");
                    expr = new MemberExpression(op.Location, expr, member.Text, op.Kind == TokenKind.Arrow);
                    break;
                }
                case TokenKind.PlusPlus:
                case TokenKind.MinusMinus:
                {
                    var op = Advance();
                    expr = new PostfixExpression(op.Location, op.Kind, expr);
                    break;
                }
                default:
                    return expr;
            }
        }
    }

    private Expression ParsePrimary()
    {
        var t = Current;
        switch (t.Kind)
        {
            case TokenKind.Identifier:
                Advance();
                return new IdentifierExpression(t.Location, t.Text);
            case TokenKind.IntegerConstant:
                Advance();
                return new IntegerLiteral(t.Location, t.Text);
            case TokenKind.FloatingConstant:
                Advance();
                return new FloatingLiteral(t.Location, t.Text);
            case TokenKind.CharacterConstant:
                Advance();
                return new CharacterLiteral(t.Location, t.Text);
            case TokenKind.StringLiteral:
            {
                var parts = new List<string>();
                var loc = t.Location;
                while (Check(TokenKind.StringLiteral))
                    parts.Add(Advance().Text); // adjacent string literals concatenate
                return new StringLiteral(loc, parts);
            }
            case TokenKind.LParen:
            {
                Advance();
                var inner = ParseExpression();
                Expect(TokenKind.RParen, "')'");
                return inner;
            }
            default:
                throw Error("expected an expression");
        }
    }
}
