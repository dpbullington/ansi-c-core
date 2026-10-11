using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;

namespace AnsiCCore.Frontend.Preprocessing;

/// <summary>
/// Recursive-descent evaluator for a <c>#if</c>/<c>#elif</c> controlling constant expression.
/// Input tokens must already have had <c>defined</c> resolved and macros expanded; any
/// remaining identifier (including keyword-spelled ones) evaluates to 0 per C89 §3.8.1.
/// Arithmetic follows C89's usual arithmetic conversions in <c>long</c>/<c>unsigned long</c>.
/// </summary>
internal sealed class PpConditionEvaluator
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly DiagnosticBag _diagnostics;
    private readonly SourceLocation _directiveLocation;
    private int _pos;
    private bool _failed;

    private PpConditionEvaluator(IReadOnlyList<Token> tokens, DiagnosticBag diagnostics, SourceLocation directiveLocation)
    {
        _tokens = tokens;
        _diagnostics = diagnostics;
        _directiveLocation = directiveLocation;
    }

    public static bool Evaluate(IReadOnlyList<Token> tokens, DiagnosticBag diagnostics, SourceLocation directiveLocation)
    {
        if (tokens.Count == 0)
        {
            diagnostics.Error(directiveLocation, "#if with no expression");
            return false;
        }

        var evaluator = new PpConditionEvaluator(tokens, diagnostics, directiveLocation);
        var value = evaluator.ParseConditional(evaluate: true);
        if (!evaluator._failed && !evaluator.AtEnd)
            evaluator.Fail($"unexpected token '{evaluator.Current.Text}' in #if expression");
        return !evaluator._failed && value.IsTrue;
    }

    private Token Current => _pos < _tokens.Count ? _tokens[_pos] : default;
    private bool AtEnd => _pos >= _tokens.Count;
    private TokenKind Kind => _pos < _tokens.Count ? _tokens[_pos].Kind : TokenKind.EndOfFile;

    private void Fail(string message)
    {
        if (_failed) return;
        _failed = true;
        var loc = _pos < _tokens.Count ? _tokens[_pos].Location : _directiveLocation;
        _diagnostics.Error(loc, message);
    }

    private bool Match(TokenKind kind)
    {
        if (Kind == kind) { _pos++; return true; }
        return false;
    }

    // conditional := logical-or ( '?' expr ':' conditional )?
    private PpValue ParseConditional(bool evaluate)
    {
        var cond = ParseBinary(0, evaluate);
        if (Match(TokenKind.Question))
        {
            bool takeThen = evaluate && cond.IsTrue;
            var thenV = ParseConditional(takeThen);
            if (!Match(TokenKind.Colon)) { Fail("expected ':' in conditional expression"); return PpValue.Zero; }
            var elseV = ParseConditional(evaluate && !takeThen);
            return cond.IsTrue ? thenV : elseV;
        }
        return cond;
    }

    // Precedence-climbing over binary operators (|| is lowest, * / % highest).
    private PpValue ParseBinary(int minPrec, bool evaluate)
    {
        var left = ParseUnary(evaluate);
        while (true)
        {
            var op = Kind;
            int prec = Precedence(op);
            if (prec < minPrec || prec == 0) break;
            _pos++;

            // Short-circuit: the dead side is parsed but not evaluated.
            bool rightEvaluate = evaluate;
            if (op == TokenKind.AmpAmp && !left.IsTrue) rightEvaluate = false;
            if (op == TokenKind.PipePipe && left.IsTrue) rightEvaluate = false;

            var right = ParseBinary(prec + 1, rightEvaluate);
            left = evaluate ? Apply(op, left, right) : PpValue.Zero;
        }
        return left;
    }

    private PpValue ParseUnary(bool evaluate)
    {
        switch (Kind)
        {
            case TokenKind.Plus: _pos++; return ParseUnary(evaluate);
            case TokenKind.Minus:
            {
                _pos++;
                var v = ParseUnary(evaluate);
                return v.IsUnsigned ? PpValue.Unsigned(unchecked(0UL - v.U)) : PpValue.Signed(unchecked(-v.Bits));
            }
            case TokenKind.Tilde:
            {
                _pos++;
                var v = ParseUnary(evaluate);
                return v with { Bits = ~v.Bits };
            }
            case TokenKind.Bang:
            {
                _pos++;
                var v = ParseUnary(evaluate);
                return PpValue.Bool(!v.IsTrue);
            }
            default:
                return ParsePrimary(evaluate);
        }
    }

    private PpValue ParsePrimary(bool evaluate)
    {
        if (Match(TokenKind.LParen))
        {
            var v = ParseConditional(evaluate);
            if (!Match(TokenKind.RParen)) Fail("expected ')' in #if expression");
            return v;
        }

        var tok = Current;
        switch (tok.Kind)
        {
            case TokenKind.IntegerConstant:
            {
                _pos++;
                var v = PpLiteral.DecodeInteger(tok.Text, out var error);
                if (error != null) _diagnostics.Error(tok.Location, error);
                return v;
            }
            case TokenKind.CharacterConstant:
            {
                _pos++;
                var v = PpLiteral.DecodeCharacter(tok.Text, out var error);
                if (error != null) _diagnostics.Error(tok.Location, error);
                return v;
            }
            case TokenKind.FloatingConstant:
                _pos++;
                Fail("floating constant in preprocessor expression");
                return PpValue.Zero;
            case TokenKind.StringLiteral:
                _pos++;
                Fail("string literal in preprocessor expression");
                return PpValue.Zero;
            default:
                if (PpFacts.IsIdentifierLike(tok))
                {
                    // Any identifier surviving macro expansion is replaced by 0.
                    _pos++;
                    return PpValue.Zero;
                }
                Fail(AtEnd ? "expected value in #if expression" : $"unexpected token '{tok.Text}' in #if expression");
                return PpValue.Zero;
        }
    }

    private static int Precedence(TokenKind op) => op switch
    {
        TokenKind.Star or TokenKind.Slash or TokenKind.Percent => 10,
        TokenKind.Plus or TokenKind.Minus => 9,
        TokenKind.LessLess or TokenKind.GreaterGreater => 8,
        TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual => 7,
        TokenKind.EqualEqual or TokenKind.BangEqual => 6,
        TokenKind.Ampersand => 5,
        TokenKind.Caret => 4,
        TokenKind.Pipe => 3,
        TokenKind.AmpAmp => 2,
        TokenKind.PipePipe => 1,
        _ => 0,
    };

    private PpValue Apply(TokenKind op, PpValue l, PpValue r)
    {
        // && / || always yield a signed 0/1.
        if (op == TokenKind.AmpAmp) return PpValue.Bool(l.IsTrue && r.IsTrue);
        if (op == TokenKind.PipePipe) return PpValue.Bool(l.IsTrue || r.IsTrue);

        bool unsigned = l.IsUnsigned || r.IsUnsigned;

        switch (op)
        {
            case TokenKind.Star: return Arith(unsigned, l, r, static (a, b) => a * b, static (a, b) => a * b);
            case TokenKind.Plus: return Arith(unsigned, l, r, static (a, b) => a + b, static (a, b) => a + b);
            case TokenKind.Minus: return Arith(unsigned, l, r, static (a, b) => a - b, static (a, b) => a - b);
            case TokenKind.Ampersand: return Arith(unsigned, l, r, static (a, b) => a & b, static (a, b) => a & b);
            case TokenKind.Pipe: return Arith(unsigned, l, r, static (a, b) => a | b, static (a, b) => a | b);
            case TokenKind.Caret: return Arith(unsigned, l, r, static (a, b) => a ^ b, static (a, b) => a ^ b);

            case TokenKind.Slash:
            case TokenKind.Percent:
            {
                bool isDiv = op == TokenKind.Slash;
                if (unsigned)
                {
                    if (r.U == 0) { DivByZero(isDiv); return PpValue.Zero; }
                    return PpValue.Unsigned(isDiv ? l.U / r.U : l.U % r.U);
                }
                if (r.Bits == 0) { DivByZero(isDiv); return PpValue.Zero; }
                // Guard the one overflowing signed case.
                if (l.Bits == long.MinValue && r.Bits == -1)
                    return PpValue.Signed(isDiv ? long.MinValue : 0);
                return PpValue.Signed(isDiv ? l.Bits / r.Bits : l.Bits % r.Bits);
            }

            case TokenKind.LessLess:
                return l.IsUnsigned ? PpValue.Unsigned(unchecked(l.U << ShiftCount(r)))
                                    : PpValue.Signed(unchecked(l.Bits << ShiftCount(r)));
            case TokenKind.GreaterGreater:
                return l.IsUnsigned ? PpValue.Unsigned(l.U >> ShiftCount(r))
                                    : PpValue.Signed(l.Bits >> ShiftCount(r));

            case TokenKind.Less: return Compare(unsigned, l, r, static (a, b) => a < b, static (a, b) => a < b);
            case TokenKind.LessEqual: return Compare(unsigned, l, r, static (a, b) => a <= b, static (a, b) => a <= b);
            case TokenKind.Greater: return Compare(unsigned, l, r, static (a, b) => a > b, static (a, b) => a > b);
            case TokenKind.GreaterEqual: return Compare(unsigned, l, r, static (a, b) => a >= b, static (a, b) => a >= b);
            case TokenKind.EqualEqual: return PpValue.Bool(l.Bits == r.Bits);
            case TokenKind.BangEqual: return PpValue.Bool(l.Bits != r.Bits);

            default:
                Fail("unsupported operator in #if expression");
                return PpValue.Zero;
        }
    }

    private static int ShiftCount(PpValue r) => (int)(r.Bits & 63);

    private static PpValue Arith(bool unsigned, PpValue l, PpValue r, Func<ulong, ulong, ulong> fu, Func<long, long, long> fs)
        => unsigned ? PpValue.Unsigned(unchecked(fu(l.U, r.U))) : PpValue.Signed(unchecked(fs(l.Bits, r.Bits)));

    private static PpValue Compare(bool unsigned, PpValue l, PpValue r, Func<ulong, ulong, bool> fu, Func<long, long, bool> fs)
        => PpValue.Bool(unsigned ? fu(l.U, r.U) : fs(l.Bits, r.Bits));

    private void DivByZero(bool isDiv)
        => Fail(isDiv ? "division by zero in #if expression" : "remainder by zero in #if expression");
}
