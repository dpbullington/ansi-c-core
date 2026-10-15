using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;
using AnsiCCore.Semantics.Symbols;
using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Semantics.Analysis;

public sealed partial class SemanticAnalyzer
{
    /// <summary>A typed expression: its type and whether it is an lvalue.</summary>
    private readonly record struct ExprInfo(QualType Type, bool IsLvalue)
    {
        public CType T => Type.Type;
    }

    private static readonly QualType SizeType = new(CTypes.UnsignedLong);   // size_t
    private static readonly QualType PtrDiffType = new(CTypes.Long);        // ptrdiff_t

    /// <summary>Types an expression, records the result for Emit, and returns it (undecayed, with lvalue-ness).</summary>
    private ExprInfo TypeExpression(Expression expression) => TypeExpr(expression, inCall: false);

    private ExprInfo Record(Expression e, QualType type, bool isLvalue)
    {
        _expressionTypes[e] = new ExpressionType(type, isLvalue);
        return new ExprInfo(type, isLvalue);
    }

    private ExprInfo Error(Expression e, SourceLocation loc, string message)
    {
        Error(loc, message);
        return Record(e, new QualType(CTypes.Error), isLvalue: false);
    }

    /// <summary>Lvalue conversion + array/function decay: yields an rvalue, unqualified, pointer for arrays/functions.</summary>
    private static ExprInfo Decay(ExprInfo e)
        => new(TypeOperations.Decay(e.Type).Unqualified, IsLvalue: false);

    private ExprInfo TypeExpr(Expression e, bool inCall)
    {
        switch (e)
        {
            case IntegerLiteral i: return Record(e, TypeIntegerLiteral(i.Text), false);
            case FloatingLiteral f: return Record(e, TypeFloatingLiteral(f.Text), false);
            case CharacterLiteral: return Record(e, new QualType(CTypes.Int), false); // char constants have type int in C
            case StringLiteral s: return Record(e, new QualType(new ArrayType(CTypes.Char, StringArrayLength(s.Parts))), true);
            case IdentifierExpression id: return TypeIdentifier(id, inCall);
            case CallExpression call: return TypeCall(call);
            case IndexExpression idx: return TypeIndex(idx);
            case MemberExpression m: return TypeMember(m);
            case UnaryExpression u: return TypeUnary(u);
            case PostfixExpression p: return TypeIncDec(p, p.Operand, p.Operator, isPostfix: true);
            case SizeOfExpression so: return TypeSizeofExpr(so);
            case SizeOfTypeExpression st: return TypeSizeofType(st);
            case CastExpression cast: return TypeCast(cast);
            case BinaryExpression b: return TypeBinary(b);
            case ConditionalExpression c: return TypeConditional(c);
            case AssignmentExpression a: return TypeAssignment(a);
            case CommaExpression comma:
                TypeExpression(comma.Left);
                var right = Decay(TypeExpression(comma.Right));
                return Record(e, right.Type, false);
            default:
                return Record(e, new QualType(CTypes.Error), false);
        }
    }

    // --- primaries --------------------------------------------------------

    private ExprInfo TypeIdentifier(IdentifierExpression id, bool inCall)
    {
        var sym = _current.LookupOrdinary(id.Name);
        if (sym is not null)
        {
            _symbols[id] = sym; // record the use -> declaration binding for Emit
            bool isLvalue = sym.Kind is SymbolKind.Variable or SymbolKind.Parameter;
            return Record(id, sym.Type, isLvalue);
        }

        if (inCall)
        {
            // C89 §3.3.2.2: a call to an undeclared function implicitly declares int f().
            Warn(id.Location, $"implicit declaration of function '{id.Name}'");
            var implicitType = new QualType(new FunctionType(CTypes.Int, [], isVariadic: false, hasPrototype: false));
            var implicitSym = new Symbol(id.Name, SymbolKind.Function, implicitType, StorageClass.Extern, id.Location);
            _global.DeclareOrdinary(implicitSym);
            _symbols[id] = implicitSym;
            return Record(id, implicitType, isLvalue: false);
        }

        return Error(id, id.Location, $"'{id.Name}' undeclared");
    }

    private ExprInfo TypeCall(CallExpression call)
    {
        var callee = TypeExpr(call.Callee, inCall: true);
        var decayed = Decay(callee).Type;

        FunctionType? function = decayed.Type switch
        {
            PointerType { Pointee.Type: FunctionType f } => f,
            FunctionType f => f,
            _ => null,
        };

        var args = call.Arguments.Select(a => (Expr: a, Info: Decay(TypeExpression(a)))).ToList();

        if (function is null)
        {
            if (!TypeOperations.IsError(callee.T))
                Error(call.Location, "called object is not a function or function pointer");
            return Record(call, new QualType(CTypes.Error), false);
        }

        if (function.HasPrototype)
        {
            bool countOk = function.IsVariadic ? args.Count >= function.Parameters.Count : args.Count == function.Parameters.Count;
            if (!countOk)
                Error(call.Location, $"function expects {function.Parameters.Count} argument(s) but {args.Count} given");

            int fixedCount = Math.Min(args.Count, function.Parameters.Count);
            for (int k = 0; k < fixedCount; k++)
                CheckAssignable(function.Parameters[k].Type, args[k].Info, args[k].Expr.Location, $"argument {k + 1}", args[k].Expr);
        }

        return Record(call, function.ReturnType, isLvalue: false);
    }

    private ExprInfo TypeIndex(IndexExpression idx)
    {
        var a = Decay(TypeExpression(idx.Array));
        var b = Decay(TypeExpression(idx.Index));

        if (a.T is PointerType pa && TypeOperations.IsInteger(b.T))
            return Record(idx, pa.Pointee, isLvalue: true);
        if (b.T is PointerType pb && TypeOperations.IsInteger(a.T))
            return Record(idx, pb.Pointee, isLvalue: true);

        if (TypeOperations.IsError(a.T) || TypeOperations.IsError(b.T))
            return Record(idx, new QualType(CTypes.Error), false);
        return Error(idx, idx.Location, "subscripted value is not an array or pointer");
    }

    private ExprInfo TypeMember(MemberExpression m)
    {
        var target = TypeExpression(m.Target);
        RecordType? record;
        TypeQualifiers baseQuals;
        bool resultLvalue;

        if (m.Arrow)
        {
            var ptr = Decay(target).T as PointerType;
            if (ptr is null)
                return TypeOperations.IsError(target.T) ? Record(m, new QualType(CTypes.Error), false)
                    : Error(m, m.Location, "'->' operand is not a pointer");
            record = ptr.Pointee.Type as RecordType;
            baseQuals = ptr.Pointee.Qualifiers;
            resultLvalue = true;
        }
        else
        {
            record = target.T as RecordType;
            baseQuals = target.Type.Qualifiers;
            resultLvalue = target.IsLvalue;
        }

        if (record is null)
            return TypeOperations.IsError(target.T) ? Record(m, new QualType(CTypes.Error), false)
                : Error(m, m.Location, $"request for member '{m.Member}' in something not a structure or union");
        if (!record.IsComplete)
            return Error(m, m.Location, $"incomplete type '{record.Display}' has no member '{m.Member}'");

        var field = record.FindField(m.Member);
        if (field is null)
            return Error(m, m.Location, $"no member named '{m.Member}' in '{record.Display}'");

        // A member access inherits the aggregate's qualifiers.
        return Record(m, field.Type.With(baseQuals), resultLvalue);
    }

    // --- unary ------------------------------------------------------------

    private ExprInfo TypeUnary(UnaryExpression u)
    {
        switch (u.Operator)
        {
            case TokenKind.Ampersand:
            {
                var operand = TypeExpression(u.Operand); // no decay: we want the real object/function
                if (!operand.IsLvalue && operand.T is not FunctionType)
                {
                    if (TypeOperations.IsError(operand.T)) return Record(u, new QualType(CTypes.Error), false);
                    return Error(u, u.Location, "lvalue required as unary '&' operand");
                }
                return Record(u, new QualType(new PointerType(operand.Type)), isLvalue: false);
            }
            case TokenKind.Star:
            {
                var operand = Decay(TypeExpression(u.Operand));
                if (operand.T is PointerType p)
                    return Record(u, p.Pointee, isLvalue: true);
                if (TypeOperations.IsError(operand.T)) return Record(u, new QualType(CTypes.Error), false);
                return Error(u, u.Location, "indirection requires a pointer operand");
            }
            case TokenKind.Plus:
            case TokenKind.Minus:
            {
                var operand = Decay(TypeExpression(u.Operand));
                if (TypeOperations.IsArithmetic(operand.T))
                    return Record(u, new QualType(TypeOperations.Promote(operand.T)), false);
                return Unusable(u, operand, "unary arithmetic operator requires an arithmetic operand");
            }
            case TokenKind.Tilde:
            {
                var operand = Decay(TypeExpression(u.Operand));
                if (TypeOperations.IsInteger(operand.T))
                    return Record(u, new QualType(TypeOperations.Promote(operand.T)), false);
                return Unusable(u, operand, "'~' requires an integer operand");
            }
            case TokenKind.Bang:
            {
                var operand = Decay(TypeExpression(u.Operand));
                if (TypeOperations.IsScalar(operand.T))
                    return Record(u, new QualType(CTypes.Int), false);
                return Unusable(u, operand, "'!' requires a scalar operand");
            }
            case TokenKind.PlusPlus:
            case TokenKind.MinusMinus:
                return TypeIncDec(u, u.Operand, u.Operator, isPostfix: false);
            default:
                return Record(u, new QualType(CTypes.Error), false);
        }
    }

    private ExprInfo TypeIncDec(Expression node, Expression operandExpr, TokenKind op, bool isPostfix)
    {
        var operand = TypeExpression(operandExpr);
        if (TypeOperations.IsError(operand.T))
            return Record(node, new QualType(CTypes.Error), false);
        if (!IsModifiable(operand))
            return Error(node, node.Location, $"'{TokenSpelling.Spell(op)}' requires a modifiable lvalue");
        if (!TypeOperations.IsScalar(operand.T))
            return Error(node, node.Location, $"'{TokenSpelling.Spell(op)}' requires a scalar operand");
        return Record(node, operand.Type.Unqualified, isLvalue: false);
    }

    private ExprInfo TypeSizeofExpr(SizeOfExpression so)
    {
        var operand = TypeExpression(so.Operand); // unevaluated; array/function keep their natural type
        if (!TypeOperations.IsError(operand.T) && (operand.T is FunctionType || !operand.T.IsComplete))
            Error(so.Location, $"invalid application of 'sizeof' to an incomplete or function type '{operand.Type}'");
        return Record(so, SizeType, false);
    }

    private ExprInfo TypeSizeofType(SizeOfTypeExpression st)
    {
        var type = ResolveTypeName(st.Type);
        if (type.Type is FunctionType || !type.Type.IsComplete)
            Error(st.Location, $"invalid application of 'sizeof' to an incomplete or function type '{type}'");
        return Record(st, SizeType, false);
    }

    private ExprInfo TypeCast(CastExpression cast)
    {
        var target = ResolveTypeName(cast.Type);
        var operand = Decay(TypeExpression(cast.Operand));

        if (!TypeOperations.IsError(target.Type) && !TypeOperations.IsError(operand.T))
        {
            bool ok = TypeOperations.IsVoid(target.Type)
                || (TypeOperations.IsScalar(target.Type) && TypeOperations.IsScalar(operand.T));
            if (!ok)
                Error(cast.Location, $"cannot cast '{operand.Type}' to '{target}'");
        }
        return Record(cast, target, isLvalue: false);
    }

    // --- binary -----------------------------------------------------------

    private ExprInfo TypeBinary(BinaryExpression b)
    {
        var l = Decay(TypeExpression(b.Left));
        var r = Decay(TypeExpression(b.Right));

        if (TypeOperations.IsError(l.T) || TypeOperations.IsError(r.T))
            return Record(b, new QualType(CTypes.Error), false);

        switch (b.Operator)
        {
            case TokenKind.Star:
            case TokenKind.Slash:
                return ArithmeticResult(b, l, r, "operands must be arithmetic");
            case TokenKind.Percent:
            case TokenKind.Ampersand:
            case TokenKind.Caret:
            case TokenKind.Pipe:
                return IntegerResult(b, l, r);
            case TokenKind.LessLess:
            case TokenKind.GreaterGreater:
                if (TypeOperations.IsInteger(l.T) && TypeOperations.IsInteger(r.T))
                    return Record(b, new QualType(TypeOperations.Promote(l.T)), false);
                return Error(b, b.Location, "shift operands must be integers");
            case TokenKind.Plus:
                return TypeAdditive(b, l, r, subtract: false);
            case TokenKind.Minus:
                return TypeAdditive(b, l, r, subtract: true);
            case TokenKind.Less:
            case TokenKind.LessEqual:
            case TokenKind.Greater:
            case TokenKind.GreaterEqual:
                return RelationalResult(b, l, r);
            case TokenKind.EqualEqual:
            case TokenKind.BangEqual:
                return EqualityResult(b, l, r);
            case TokenKind.AmpAmp:
            case TokenKind.PipePipe:
                if (TypeOperations.IsScalar(l.T) && TypeOperations.IsScalar(r.T))
                    return Record(b, new QualType(CTypes.Int), false);
                return Error(b, b.Location, "logical operator requires scalar operands");
            default:
                return Record(b, new QualType(CTypes.Error), false);
        }
    }

    private ExprInfo ArithmeticResult(BinaryExpression b, ExprInfo l, ExprInfo r, string message)
    {
        if (TypeOperations.IsArithmetic(l.T) && TypeOperations.IsArithmetic(r.T))
            return Record(b, new QualType(TypeOperations.UsualArithmeticConversions(l.T, r.T)), false);
        return Error(b, b.Location, message);
    }

    private ExprInfo IntegerResult(BinaryExpression b, ExprInfo l, ExprInfo r)
    {
        if (TypeOperations.IsInteger(l.T) && TypeOperations.IsInteger(r.T))
            return Record(b, new QualType(TypeOperations.UsualArithmeticConversions(l.T, r.T)), false);
        return Error(b, b.Location, $"'{TokenSpelling.Spell(b.Operator)}' requires integer operands");
    }

    private ExprInfo TypeAdditive(BinaryExpression b, ExprInfo l, ExprInfo r, bool subtract)
    {
        bool lArith = TypeOperations.IsArithmetic(l.T), rArith = TypeOperations.IsArithmetic(r.T);
        if (lArith && rArith)
            return Record(b, new QualType(TypeOperations.UsualArithmeticConversions(l.T, r.T)), false);

        if (l.T is PointerType && TypeOperations.IsInteger(r.T))
            return Record(b, l.Type, false);                  // pointer ± integer → pointer

        if (!subtract && TypeOperations.IsInteger(l.T) && r.T is PointerType)
            return Record(b, r.Type, false);                  // integer + pointer → pointer

        if (subtract && l.T is PointerType lp && r.T is PointerType rp)
        {
            if (!TypeOperations.AreCompatible(lp.Pointee.Type, rp.Pointee.Type))
                Error(b.Location, "subtraction of incompatible pointer types");
            return Record(b, PtrDiffType, false);             // pointer − pointer → ptrdiff_t
        }

        return Error(b, b.Location, subtract ? "invalid operands to '-'" : "invalid operands to '+'");
    }

    private ExprInfo RelationalResult(BinaryExpression b, ExprInfo l, ExprInfo r)
    {
        bool ok = (TypeOperations.IsArithmetic(l.T) && TypeOperations.IsArithmetic(r.T))
                  || (l.T is PointerType && r.T is PointerType);
        if (!ok) Error(b.Location, "invalid operands to relational operator");
        return Record(b, new QualType(CTypes.Int), false);
    }

    private ExprInfo EqualityResult(BinaryExpression b, ExprInfo l, ExprInfo r)
    {
        bool ok = (TypeOperations.IsArithmetic(l.T) && TypeOperations.IsArithmetic(r.T))
                  || (l.T is PointerType && r.T is PointerType)
                  || (l.T is PointerType && IsNullPointerConstant(b.Right))
                  || (r.T is PointerType && IsNullPointerConstant(b.Left));
        if (!ok) Error(b.Location, "invalid operands to equality operator");
        return Record(b, new QualType(CTypes.Int), false);
    }

    // --- conditional / assignment ----------------------------------------

    private ExprInfo TypeConditional(ConditionalExpression c)
    {
        var cond = Decay(TypeExpression(c.Condition));
        if (!TypeOperations.IsError(cond.T) && !TypeOperations.IsScalar(cond.T))
            Error(c.Condition.Location, "condition of '?:' must be scalar");

        var a = Decay(TypeExpression(c.WhenTrue));
        var d = Decay(TypeExpression(c.WhenFalse));

        if (TypeOperations.IsError(a.T) || TypeOperations.IsError(d.T))
            return Record(c, new QualType(CTypes.Error), false);
        if (TypeOperations.IsArithmetic(a.T) && TypeOperations.IsArithmetic(d.T))
            return Record(c, new QualType(TypeOperations.UsualArithmeticConversions(a.T, d.T)), false);
        if (TypeOperations.IsVoid(a.T) && TypeOperations.IsVoid(d.T))
            return Record(c, new QualType(CTypes.Void), false);
        if (a.T is PointerType && (d.T is PointerType || IsNullPointerConstant(c.WhenFalse)))
            return Record(c, a.Type, false);
        if (d.T is PointerType && IsNullPointerConstant(c.WhenTrue))
            return Record(c, d.Type, false);
        if (TypeOperations.AreCompatible(a.T, d.T))
            return Record(c, a.Type, false);

        Error(c.Location, "incompatible operand types in '?:'");
        return Record(c, a.Type, false);
    }

    private ExprInfo TypeAssignment(AssignmentExpression a)
    {
        var target = TypeExpression(a.Target);
        var value = Decay(TypeExpression(a.Value));

        if (TypeOperations.IsError(target.T))
            return Record(a, new QualType(CTypes.Error), false);

        if (!IsModifiable(target))
            return Error(a, a.Location, "assignment to a non-modifiable lvalue");

        if (a.Operator == TokenKind.Equal)
        {
            CheckAssignable(target.Type, value, a.Location, "assignment", a.Value);
        }
        else if (a.Operator is TokenKind.PlusEqual or TokenKind.MinusEqual && target.T is PointerType && TypeOperations.IsInteger(value.T))
        {
            // pointer += / -= integer is fine
        }
        else if (!TypeOperations.IsScalar(target.T) || !TypeOperations.IsScalar(value.T))
        {
            Error(a.Location, "invalid operands to compound assignment");
        }

        return Record(a, target.Type.Unqualified, isLvalue: false);
    }

    // --- assignment compatibility ----------------------------------------

    private void CheckAssignable(QualType target, ExprInfo source, SourceLocation loc, string context, Expression? sourceExpr = null)
    {
        CType tt = target.Type, st = source.T;
        if (TypeOperations.IsError(tt) || TypeOperations.IsError(st)) return;

        if (TypeOperations.IsArithmetic(tt) && TypeOperations.IsArithmetic(st)) return;

        if (tt is PointerType tp)
        {
            if (sourceExpr is not null && IsNullPointerConstant(sourceExpr)) return;
            if (st is PointerType sp)
            {
                bool eitherVoid = TypeOperations.IsVoid(tp.Pointee.Type) || TypeOperations.IsVoid(sp.Pointee.Type);
                if (eitherVoid || TypeOperations.AreCompatible(tp.Pointee.Type, sp.Pointee.Type))
                {
                    // Flag a pointer conversion that drops qualifiers from the pointee.
                    if (!eitherVoid && (sp.Pointee.Qualifiers & ~tp.Pointee.Qualifiers) != 0)
                        Warn(loc, $"{context} discards pointer qualifiers");
                    return;
                }
                Warn(loc, $"{context}: incompatible pointer types ('{target}' from '{source.Type}')");
                return;
            }
            if (TypeOperations.IsInteger(st))
            {
                Warn(loc, $"{context} makes pointer from integer without a cast");
                return;
            }
        }
        else if (tt is RecordType && TypeOperations.AreCompatible(tt, st))
        {
            return;
        }

        Error(loc, $"incompatible types in {context} ('{target}' from '{source.Type}')");
    }

    // --- helpers ----------------------------------------------------------

    private ExprInfo Unusable(Expression node, ExprInfo operand, string message)
        => TypeOperations.IsError(operand.T)
            ? Record(node, new QualType(CTypes.Error), false)
            : Error(node, node.Location, message);

    private static bool IsModifiable(ExprInfo e)
        => e.IsLvalue
           && !e.Type.IsConst
           && e.T is not ArrayType and not FunctionType
           && e.T.IsComplete;

    private bool IsNullPointerConstant(Expression e)
    {
        var evaluator = new ConstantEvaluator(_current, ResolveTypeName);
        return evaluator.TryEvaluate(e, out long value) && value == 0;
    }

    // --- literal typing ---------------------------------------------------

    private static QualType TypeFloatingLiteral(string text)
    {
        char last = text.Length > 0 ? text[^1] : '\0';
        return new QualType(last switch
        {
            'f' or 'F' => CTypes.Float,
            'l' or 'L' => CTypes.LongDouble,
            _ => CTypes.Double,
        });
    }

    private static QualType TypeIntegerLiteral(string text)
    {
        bool u = false, l = false;
        int end = text.Length;
        while (end > 0 && text[end - 1] is 'u' or 'U' or 'l' or 'L')
        {
            if (text[end - 1] is 'u' or 'U') u = true; else l = true;
            end--;
        }

        string core = text[..end];
        bool hexOrOctal = core.StartsWith('0') && core.Length > 1;
        ulong value;
        try
        {
            value = core.Length >= 2 && (core[1] is 'x' or 'X') ? Convert.ToUInt64(core[2..], 16)
                  : hexOrOctal ? Convert.ToUInt64(core, 8)
                  : Convert.ToUInt64(core, 10);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return new QualType(CTypes.Int);
        }

        IntegerType type =
            u && l ? CTypes.UnsignedLong
            : l ? (value <= long.MaxValue ? CTypes.Long : CTypes.UnsignedLong)
            : u ? (value <= uint.MaxValue ? CTypes.UnsignedInt : CTypes.UnsignedLong)
            : value <= int.MaxValue ? CTypes.Int
            : hexOrOctal && value <= uint.MaxValue ? CTypes.UnsignedInt
            : value <= long.MaxValue ? CTypes.Long
            : CTypes.UnsignedLong;

        return new QualType(type);
    }

    private static long StringArrayLength(IReadOnlyList<string> parts)
    {
        long count = 0;
        foreach (var part in parts)
        {
            int i = 0;
            if (i < part.Length && part[i] == 'L') i++;   // wide prefix (treated as char[] for now)
            if (i < part.Length && part[i] == '"') i++;
            int end = part.EndsWith('"') ? part.Length - 1 : part.Length;
            while (i < end)
            {
                if (part[i] == '\\' && i + 1 < end) i++; // an escape contributes a single char
                i++;
                count++;
            }
        }
        return count + 1; // terminating NUL
    }
}
