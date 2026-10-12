using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;

namespace AnsiCCore.Frontend.Parsing;

public sealed partial class Parser
{
    private CompoundStatement ParseCompoundStatement()
    {
        var loc = Expect(TokenKind.LBrace, "'{'").Location;
        PushScope();

        var items = new List<Node>();
        while (!Check(TokenKind.RBrace) && !AtEnd)
        {
            try
            {
                items.Add(IsDeclarationStart() ? ParseBlockDeclaration() : ParseStatement());
            }
            catch (ParseError)
            {
                SynchronizeStatement();
                Match(TokenKind.Semicolon);
            }
        }

        PopScope();
        Expect(TokenKind.RBrace, "'}' to close block");
        return new CompoundStatement(loc, items);
    }

    private Statement ParseStatement()
    {
        switch (Kind)
        {
            case TokenKind.LBrace:
                return ParseCompoundStatement();

            case TokenKind.KwIf:
                return ParseIf();
            case TokenKind.KwSwitch:
                return ParseSwitch();
            case TokenKind.KwWhile:
                return ParseWhile();
            case TokenKind.KwDo:
                return ParseDoWhile();
            case TokenKind.KwFor:
                return ParseFor();

            case TokenKind.KwGoto:
            {
                var loc = Advance().Location;
                var label = Expect(TokenKind.Identifier, "label name").Text;
                Expect(TokenKind.Semicolon, "';' after goto");
                return new GotoStatement(loc, label);
            }
            case TokenKind.KwContinue:
            {
                var loc = Advance().Location;
                Expect(TokenKind.Semicolon, "';' after continue");
                return new ContinueStatement(loc);
            }
            case TokenKind.KwBreak:
            {
                var loc = Advance().Location;
                Expect(TokenKind.Semicolon, "';' after break");
                return new BreakStatement(loc);
            }
            case TokenKind.KwReturn:
            {
                var loc = Advance().Location;
                Expression? value = Check(TokenKind.Semicolon) ? null : ParseExpression();
                Expect(TokenKind.Semicolon, "';' after return");
                return new ReturnStatement(loc, value);
            }

            case TokenKind.KwCase:
            {
                var loc = Advance().Location;
                var value = ParseConstantExpression();
                Expect(TokenKind.Colon, "':' after case label");
                return new CaseStatement(loc, value, ParseStatement());
            }
            case TokenKind.KwDefault:
            {
                var loc = Advance().Location;
                Expect(TokenKind.Colon, "':' after default");
                return new DefaultStatement(loc, ParseStatement());
            }

            case TokenKind.Identifier when Peek().Kind == TokenKind.Colon:
            {
                var label = Advance();      // identifier
                Advance();                  // ':'
                return new LabeledStatement(label.Location, label.Text, ParseStatement());
            }

            case TokenKind.Semicolon:
            {
                var loc = Advance().Location;
                return new ExpressionStatement(loc, null);
            }

            default:
            {
                var expr = ParseExpression();
                Expect(TokenKind.Semicolon, "';' after expression");
                return new ExpressionStatement(expr.Location, expr);
            }
        }
    }

    private Statement ParseIf()
    {
        var loc = Advance().Location; // if
        Expect(TokenKind.LParen, "'(' after if");
        var cond = ParseExpression();
        Expect(TokenKind.RParen, "')' after if condition");
        var then = ParseStatement();
        Statement? otherwise = Match(TokenKind.KwElse) ? ParseStatement() : null;
        return new IfStatement(loc, cond, then, otherwise);
    }

    private Statement ParseSwitch()
    {
        var loc = Advance().Location; // switch
        Expect(TokenKind.LParen, "'(' after switch");
        var expr = ParseExpression();
        Expect(TokenKind.RParen, "')' after switch expression");
        return new SwitchStatement(loc, expr, ParseStatement());
    }

    private Statement ParseWhile()
    {
        var loc = Advance().Location; // while
        Expect(TokenKind.LParen, "'(' after while");
        var cond = ParseExpression();
        Expect(TokenKind.RParen, "')' after while condition");
        return new WhileStatement(loc, cond, ParseStatement());
    }

    private Statement ParseDoWhile()
    {
        var loc = Advance().Location; // do
        var body = ParseStatement();
        Expect(TokenKind.KwWhile, "'while' after do-body");
        Expect(TokenKind.LParen, "'(' after do-while");
        var cond = ParseExpression();
        Expect(TokenKind.RParen, "')' after do-while condition");
        Expect(TokenKind.Semicolon, "';' after do-while");
        return new DoStatement(loc, body, cond);
    }

    private Statement ParseFor()
    {
        var loc = Advance().Location; // for
        Expect(TokenKind.LParen, "'(' after for");

        Expression? init = Check(TokenKind.Semicolon) ? null : ParseExpression();
        Expect(TokenKind.Semicolon, "';' after for-initializer");

        Expression? cond = Check(TokenKind.Semicolon) ? null : ParseExpression();
        Expect(TokenKind.Semicolon, "';' after for-condition");

        Expression? iter = Check(TokenKind.RParen) ? null : ParseExpression();
        Expect(TokenKind.RParen, "')' after for-clauses");

        return new ForStatement(loc, init, cond, iter, ParseStatement());
    }
}
