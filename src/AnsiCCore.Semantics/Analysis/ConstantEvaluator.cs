using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Lexing;
using AnsiCCore.Semantics.Symbols;
using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Semantics.Analysis;

/// <summary>
/// Evaluates integer constant expressions (C89 §3.4) from the AST: the values permitted as
/// array bounds, enum values, bit-field widths, and case labels. Supports integer/character
/// constants, enum constants (looked up in scope), the arithmetic/bitwise/relational/logical
/// operators, the conditional operator, and <c>sizeof(type-name)</c>. Returns null when the
/// expression is not a constant this evaluator can fold.
/// </summary>
internal sealed class ConstantEvaluator
{
    private readonly Scope _scope;
    private readonly Func<TypeName, QualType> _resolveTypeName;

    public ConstantEvaluator(Scope scope, Func<TypeName, QualType> resolveTypeName)
    {
        _scope = scope;
        _resolveTypeName = resolveTypeName;
    }

    public bool TryEvaluate(Expression expr, out long value)
    {
        var result = Eval(expr);
        value = result ?? 0;
        return result.HasValue;
    }

    private long? Eval(Expression e) => e switch
    {
        IntegerLiteral i => DecodeInteger(i.Text),
        CharacterLiteral c => DecodeCharacter(c.Text),
        IdentifierExpression id => EvalIdentifier(id),
        UnaryExpression u => EvalUnary(u),
        BinaryExpression b => EvalBinary(b),
        ConditionalExpression c => Eval(c.Condition) is { } cond ? (cond != 0 ? Eval(c.WhenTrue) : Eval(c.WhenFalse)) : null,
        SizeOfTypeExpression s => TypeOperations.SizeOf(_resolveTypeName(s.Type).Type),
        _ => null,
    };

    private long? EvalIdentifier(IdentifierExpression id)
    {
        var sym = _scope.LookupOrdinary(id.Name);
        return sym is { Kind: SymbolKind.EnumConstant } ? sym.EnumValue : null;
    }

    private long? EvalUnary(UnaryExpression u)
    {
        var v = Eval(u.Operand);
        if (v is null) return null;
        return u.Operator switch
        {
            TokenKind.Plus => v,
            TokenKind.Minus => -v,
            TokenKind.Tilde => ~v,
            TokenKind.Bang => v == 0 ? 1 : 0,
            _ => null, // &, *, ++, -- are not integer constant expressions
        };
    }

    private long? EvalBinary(BinaryExpression b)
    {
        var l = Eval(b.Left);
        if (l is null) return null;

        // Short-circuit operators must not require the dead operand to be constant.
        switch (b.Operator)
        {
            case TokenKind.AmpAmp: return l == 0 ? 0 : (Eval(b.Right) is { } r0 ? (r0 != 0 ? 1 : 0) : null);
            case TokenKind.PipePipe: return l != 0 ? 1 : (Eval(b.Right) is { } r1 ? (r1 != 0 ? 1 : 0) : null);
        }

        var r = Eval(b.Right);
        if (r is null) return null;
        long x = l.Value, y = r.Value;

        return b.Operator switch
        {
            TokenKind.Star => x * y,
            TokenKind.Slash => y == 0 ? null : x / y,
            TokenKind.Percent => y == 0 ? null : x % y,
            TokenKind.Plus => x + y,
            TokenKind.Minus => x - y,
            TokenKind.LessLess => x << (int)y,
            TokenKind.GreaterGreater => x >> (int)y,
            TokenKind.Less => x < y ? 1 : 0,
            TokenKind.LessEqual => x <= y ? 1 : 0,
            TokenKind.Greater => x > y ? 1 : 0,
            TokenKind.GreaterEqual => x >= y ? 1 : 0,
            TokenKind.EqualEqual => x == y ? 1 : 0,
            TokenKind.BangEqual => x != y ? 1 : 0,
            TokenKind.Ampersand => x & y,
            TokenKind.Caret => x ^ y,
            TokenKind.Pipe => x | y,
            _ => null,
        };
    }

    // --- literal decoding (integer constant expressions use integer/char constants) -------

    private static long? DecodeInteger(string lexeme)
    {
        int end = lexeme.Length;
        while (end > 0 && lexeme[end - 1] is 'u' or 'U' or 'l' or 'L') end--;
        string core = lexeme[..end];

        try
        {
            if (core.Length >= 2 && core[0] == '0' && (core[1] is 'x' or 'X'))
                return (long)Convert.ToUInt64(core[2..], 16);
            if (core.Length >= 2 && core[0] == '0')
                return (long)Convert.ToUInt64(core[1..], 8);
            return (long)Convert.ToUInt64(core, 10);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return null;
        }
    }

    private static long? DecodeCharacter(string lexeme)
    {
        int i = 0;
        if (i < lexeme.Length && lexeme[i] == 'L') i++;
        if (i >= lexeme.Length || lexeme[i] != '\'') return null;
        i++;
        int end = lexeme.EndsWith('\'') ? lexeme.Length - 1 : lexeme.Length;
        if (i >= end) return null;

        char c = lexeme[i++];
        if (c != '\\') return c;
        if (i >= end) return '\\';

        char esc = lexeme[i++];
        return esc switch
        {
            'n' => '\n', 't' => '\t', 'r' => '\r', 'v' => '\v', 'f' => '\f', 'b' => '\b', 'a' => '\a',
            '\\' => '\\', '\'' => '\'', '"' => '"', '?' => '?',
            '0' => 0,
            _ => esc,
        };
    }
}
