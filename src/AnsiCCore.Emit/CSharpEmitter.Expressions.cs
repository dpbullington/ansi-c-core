using System.Globalization;
using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Lexing;
using AnsiCCore.Semantics.Symbols;
using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Emit;

public sealed partial class CSharpEmitter
{
    /// <summary>Emits an expression as a C# <em>value</em> (C semantics: logical/relational yield int 0/1).</summary>
    private string EmitValue(Expression e) => e switch
    {
        IntegerLiteral i => EmitIntegerLiteral(i),
        FloatingLiteral f => EmitFloatingLiteral(f.Text),
        CharacterLiteral c => DecodeChar(c.Text).ToString(CultureInfo.InvariantCulture),
        IdentifierExpression id => EmitIdentifier(id),
        CallExpression call => EmitCall(call),
        CastExpression cast => EmitCast(cast),
        SizeOfExpression so => EmitSizeof(SizeOfOperandType(so)),
        SizeOfTypeExpression st => EmitSizeof(_model.ResolvedTypeNames.GetValueOrDefault(st.Type).Type),
        ConditionalExpression c => $"({EmitCondition(c.Condition)} ? {EmitValue(c.WhenTrue)} : {EmitValue(c.WhenFalse)})",
        AssignmentExpression a => EmitAssignment(a),
        CommaExpression comma => $"Comma({EmitValue(comma.Left)}, {EmitValue(comma.Right)})",
        UnaryExpression u => EmitUnaryValue(u),
        PostfixExpression p => $"({EmitValue(p.Operand)}{TokenSpelling.Spell(p.Operator)})",
        BinaryExpression b => EmitBinaryValue(b),
        _ => throw new EmitException($"expression '{e.GetType().Name}' is not supported yet"),
    };

    /// <summary>
    /// Emits an expression used in statement position as a C# <em>statement-expression</em>: no
    /// invalidating outer parentheses, and non-statement expressions assigned to a discard. (C
    /// permits any expression as a statement; C# permits only assignment/call/inc/dec/new.)
    /// </summary>
    private string EmitStatementExpression(Expression e)
    {
        switch (e)
        {
            case AssignmentExpression { Operator: TokenKind.Equal } a:
            {
                var targetType = _model.TypeOf(a.Target)?.Type ?? new QualType(CTypes.Int);
                return $"{EmitValue(a.Target)} = {EmitConvert(targetType, a.Value)}";
            }
            case AssignmentExpression a:
                return $"{EmitValue(a.Target)} {TokenSpelling.Spell(a.Operator)} {EmitValue(a.Value)}";
            case CallExpression:
                return EmitValue(e);
            case PostfixExpression p:
                return $"{EmitValue(p.Operand)}{TokenSpelling.Spell(p.Operator)}";
            case UnaryExpression { Operator: TokenKind.PlusPlus or TokenKind.MinusMinus } u:
                return $"{TokenSpelling.Spell(u.Operator)}{EmitValue(u.Operand)}";
            default:
                return $"_ = {EmitValue(e)}"; // discard: makes any other expression a valid statement
        }
    }

    /// <summary>Emits an expression as a comma-flattened clause for a C# <c>for</c> initializer/iterator.</summary>
    private string EmitForClause(Expression e)
        => e is CommaExpression c ? $"{EmitForClause(c.Left)}, {EmitForClause(c.Right)}" : EmitStatementExpression(e);

    /// <summary>Emits an expression as a C# <c>bool</c> (for conditions and logical operators).</summary>
    private string EmitCondition(Expression e)
    {
        switch (e)
        {
            case BinaryExpression b when IsRelational(b.Operator):
                return $"({EmitValue(b.Left)} {TokenSpelling.Spell(b.Operator)} {EmitValue(b.Right)})";
            case BinaryExpression { Operator: TokenKind.AmpAmp } b:
                return $"({EmitCondition(b.Left)} && {EmitCondition(b.Right)})";
            case BinaryExpression { Operator: TokenKind.PipePipe } b:
                return $"({EmitCondition(b.Left)} || {EmitCondition(b.Right)})";
            case UnaryExpression { Operator: TokenKind.Bang } u:
                return $"!({EmitCondition(u.Operand)})";
            default:
                return $"({EmitValue(e)} != 0)";
        }
    }

    /// <summary>Emits <paramref name="e"/> converted to <paramref name="target"/> (an explicit cast only when needed).</summary>
    private string EmitConvert(QualType target, Expression e)
    {
        string value = EmitValue(e);
        string targetCs = TypeMapper.Map(target);
        var source = _model.TypeOf(e)?.Type;
        if (source is null || TypeMapper.Map(source.Value) == targetCs)
            return value;
        return $"unchecked(({targetCs})({value}))";
    }

    // --- operator helpers -------------------------------------------------

    private static bool IsRelational(TokenKind op) => op is
        TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual
        or TokenKind.EqualEqual or TokenKind.BangEqual;

    private static bool IsBooleanOperator(TokenKind op) => IsRelational(op) || op is TokenKind.AmpAmp or TokenKind.PipePipe;

    private string EmitBinaryValue(BinaryExpression b)
    {
        if (IsBooleanOperator(b.Operator))
            return $"({EmitCondition(b)} ? 1 : 0)";
        return $"({EmitValue(b.Left)} {TokenSpelling.Spell(b.Operator)} {EmitValue(b.Right)})";
    }

    private string EmitUnaryValue(UnaryExpression u)
    {
        switch (u.Operator)
        {
            case TokenKind.Bang:
                return $"({EmitCondition(u)} ? 1 : 0)";
            case TokenKind.Plus:
            case TokenKind.Minus:
            case TokenKind.Tilde:
                return $"({TokenSpelling.Spell(u.Operator)}{EmitValue(u.Operand)})";
            case TokenKind.PlusPlus:
            case TokenKind.MinusMinus:
                return $"({TokenSpelling.Spell(u.Operator)}{EmitValue(u.Operand)})";
            default:
                throw new EmitException($"unary operator '{TokenSpelling.Spell(u.Operator)}' is not supported yet");
        }
    }

    private string EmitAssignment(AssignmentExpression a)
    {
        string target = EmitValue(a.Target);
        if (a.Operator == TokenKind.Equal)
        {
            var targetType = _model.TypeOf(a.Target)?.Type ?? new QualType(CTypes.Int);
            return $"({target} = {EmitConvert(targetType, a.Value)})";
        }
        return $"({target} {TokenSpelling.Spell(a.Operator)} {EmitValue(a.Value)})";
    }

    private string EmitCall(CallExpression call)
    {
        string callee = EmitValue(call.Callee);
        var fnType = _model.TypeOf(call.Callee)?.Type.Type as FunctionType;

        var args = call.Arguments.Select((arg, i) =>
            fnType is { HasPrototype: true } && i < fnType.Parameters.Count
                ? EmitConvert(fnType.Parameters[i].Type, arg)
                : EmitValue(arg));

        return $"{callee}({string.Join(", ", args)})";
    }

    private string EmitCast(CastExpression cast)
    {
        var target = _model.ResolvedTypeNames.GetValueOrDefault(cast.Type);
        if (target.Type is null || target.Type is VoidType)
            throw new EmitException("cast to this type is not supported yet");
        return $"unchecked(({TypeMapper.Map(target)})({EmitValue(cast.Operand)}))";
    }

    private string EmitSizeof(CType? operandType)
    {
        long? size = operandType is null ? null : TypeOperations.SizeOf(operandType);
        if (size is null)
            throw new EmitException("sizeof of an unsupported or incomplete type");
        return $"{size.Value}UL";
    }

    private CType? SizeOfOperandType(SizeOfExpression so) => _model.TypeOf(so.Operand)?.Type.Type;

    private string EmitIdentifier(IdentifierExpression id)
    {
        if (_model.SymbolsByNode.TryGetValue(id, out var sym))
        {
            if (sym.Kind == SymbolKind.EnumConstant)
                return sym.EnumValue.ToString(CultureInfo.InvariantCulture);
            if (_localNames.TryGetValue(sym, out var local))
                return local; // a renamed (shadowing) local
        }
        return Naming.Safe(id.Name);
    }

    // --- literals ---------------------------------------------------------

    private string EmitIntegerLiteral(IntegerLiteral i)
    {
        ulong value = DecodeInteger(i.Text);
        var type = _model.TypeOf(i)?.Type.Type as IntegerType ?? CTypes.Int;
        string suffix = (type.IsSigned, type.Rank >= CTypes.Long.Rank) switch
        {
            (false, true) => "UL",
            (false, false) => "U",
            (true, true) => "L",
            _ => string.Empty,
        };
        return value.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    private static string EmitFloatingLiteral(string text)
    {
        char last = text.Length > 0 ? text[^1] : '\0';
        string body = last is 'f' or 'F' or 'l' or 'L' ? text[..^1] : text;
        if (body.EndsWith('.')) body += "0";
        return last is 'f' or 'F' ? body + "f" : body; // long-double suffix dropped (maps to double)
    }

    private static ulong DecodeInteger(string text)
    {
        int end = text.Length;
        while (end > 0 && text[end - 1] is 'u' or 'U' or 'l' or 'L') end--;
        string core = text[..end];
        try
        {
            if (core.Length >= 2 && core[0] == '0' && (core[1] is 'x' or 'X')) return Convert.ToUInt64(core[2..], 16);
            if (core.Length >= 2 && core[0] == '0') return Convert.ToUInt64(core[1..], 8);
            return Convert.ToUInt64(core, 10);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return 0;
        }
    }

    private static long DecodeChar(string text)
    {
        int i = 0;
        if (i < text.Length && text[i] == 'L') i++;
        if (i >= text.Length || text[i] != '\'') return 0;
        i++;
        int end = text.EndsWith('\'') ? text.Length - 1 : text.Length;
        if (i >= end) return 0;

        char c = text[i++];
        if (c != '\\') return c;
        if (i >= end) return '\\';
        char esc = text[i];
        return esc switch
        {
            'n' => '\n', 't' => '\t', 'r' => '\r', 'v' => '\v', 'f' => '\f', 'b' => '\b', 'a' => '\a',
            '\\' => '\\', '\'' => '\'', '"' => '"', '?' => '?', '0' => 0,
            _ => esc,
        };
    }
}
