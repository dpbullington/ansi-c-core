using System.Globalization;
using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Lexing;
using AnsiCCore.Semantics.Symbols;
using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Emit;

public sealed partial class CSharpEmitter
{
    // --- value emission ---------------------------------------------------

    /// <summary>Emits an expression as a C# value (C semantics: logical/relational yield int 0/1).</summary>
    private string EmitValue(Expression e) => e switch
    {
        IntegerLiteral i => EmitIntegerLiteral(i),
        FloatingLiteral f => EmitFloatingLiteral(f.Text),
        CharacterLiteral c => DecodeChar(c.Text).ToString(CultureInfo.InvariantCulture),
        StringLiteral s => InternString(DecodeStringBytes(s)),
        IdentifierExpression id => EmitIdentifierValue(id),
        UnaryExpression u => EmitUnaryValue(u),
        PostfixExpression p => EmitIncDecValue(p.Operand, p.Operator, isPostfix: true),
        BinaryExpression b => EmitBinaryValue(b),
        ConditionalExpression c => $"({EmitCondition(c.Condition)} ? {EmitValue(c.WhenTrue)} : {EmitValue(c.WhenFalse)})",
        AssignmentExpression a => EmitAssignmentValue(a),
        CommaExpression comma => $"Comma({EmitValue(comma.Left)}, {EmitValue(comma.Right)})",
        CallExpression call => EmitCallValue(call),
        CastExpression cast => EmitCast(cast),
        IndexExpression idx => EmitLvalueValue(idx),
        MemberExpression m => EmitLvalueValue(m),
        SizeOfExpression so => EmitSizeof(_model.TypeOf(so.Operand)?.Type.Type),
        SizeOfTypeExpression st => EmitSizeof(_model.ResolvedTypeNames.GetValueOrDefault(st.Type).Type),
        _ => throw new EmitException($"expression '{e.GetType().Name}' is not supported yet"),
    };

    private string EmitIdentifierValue(IdentifierExpression id)
    {
        _model.SymbolsByNode.TryGetValue(id, out var sym);
        if (sym is { Kind: SymbolKind.EnumConstant })
            return sym.EnumValue.ToString(CultureInfo.InvariantCulture);
        if (sym is not null && _plan.IsMemoryBacked(sym))
            return EmitLvalueValue(id);
        if (sym is not null && _localNames.TryGetValue(sym, out var local))
            return local;
        return Naming.Safe(id.Name);
    }

    /// <summary>Emits the value of an lvalue: a memory read for scalars, or the address for arrays/structs (decay).</summary>
    private string EmitLvalueValue(Expression e)
    {
        if (AsBitField(e) is { } bf)
            return $"unchecked(({TypeMapper.Map(bf.FieldType)})__mem.ReadBitField({bf.UnitAddr}, {bf.UnitSize}, {bf.BitOffset}, {bf.BitWidth}, {Bool(bf.Signed)}))";

        var type = _model.TypeOf(e)?.Type.Type ?? CTypes.Int;
        if (type is ArrayType or RecordType)
            return EmitAddress(e); // array decays to a pointer; a struct is referred to by address
        return $"__mem.Read{MemoryAccess.Suffix(type)}({EmitAddress(e)})";
    }

    private static string Bool(bool b) => b ? "true" : "false";

    private readonly record struct BitFieldAccess(string UnitAddr, int UnitSize, int BitOffset, int BitWidth, bool Signed, CType FieldType);

    /// <summary>If <paramref name="e"/> is a bit-field member access, returns how to read/write its storage unit.</summary>
    private BitFieldAccess? AsBitField(Expression e)
    {
        if (e is not MemberExpression m) return null;

        RecordType? record;
        string baseAddr;
        if (m.Arrow)
        {
            record = (_model.TypeOf(m.Target)?.Type.Type as PointerType)?.Pointee.Type as RecordType;
            baseAddr = EmitValue(m.Target);
        }
        else
        {
            record = _model.TypeOf(m.Target)?.Type.Type as RecordType;
            baseAddr = EmitAddress(m.Target);
        }

        if (record?.FindField(m.Member) is not { IsBitField: true } field)
            return null;

        bool signed = field.Type.Type is not IntegerType { IsSigned: false };
        return new BitFieldAccess($"({baseAddr} + {field.Offset})", (int)MemoryAccess.SizeOf(field.Type.Type),
            field.BitOffset, field.BitWidth!.Value, signed, field.Type.Type);
    }

    private string EmitUnaryValue(UnaryExpression u)
    {
        switch (u.Operator)
        {
            case TokenKind.Ampersand: return EmitAddress(u.Operand);
            case TokenKind.Star:
                // Dereferencing a function pointer yields the function itself (the delegate).
                return TypeMapper.IsFunctionPointer(_model.TypeOf(u.Operand)?.Type.Type ?? CTypes.Int)
                    ? EmitValue(u.Operand)
                    : EmitLvalueValue(u);
            case TokenKind.Bang: return $"({EmitCondition(u)} ? 1 : 0)";
            case TokenKind.Plus:
            case TokenKind.Minus:
            case TokenKind.Tilde:
                return $"({TokenSpelling.Spell(u.Operator)}{EmitValue(u.Operand)})";
            case TokenKind.PlusPlus:
            case TokenKind.MinusMinus:
                return EmitIncDecValue(u.Operand, u.Operator, isPostfix: false);
            default:
                throw new EmitException($"unary operator '{TokenSpelling.Spell(u.Operator)}' is not supported yet");
        }
    }

    // --- address emission -------------------------------------------------

    /// <summary>Emits a CPtr-valued C# expression for an lvalue that has an address.</summary>
    private string EmitAddress(Expression e)
    {
        switch (e)
        {
            case IdentifierExpression id:
                _model.SymbolsByNode.TryGetValue(id, out var sym);
                if (sym is not null && _addressOf.TryGetValue(sym, out var a)) return a;
                if (sym is not null && _globalAddress.TryGetValue(sym, out var g)) return g;
                if (sym is { Kind: SymbolKind.Function }) return Naming.Safe(id.Name); // &func -> method group (delegate)
                throw new EmitException($"cannot take the address of '{id.Name}'");

            case UnaryExpression { Operator: TokenKind.Star } u:
                return EmitValue(u.Operand);

            case IndexExpression idx:
                return EmitElementAddress(idx);

            case MemberExpression m:
                return EmitMemberAddress(m);

            default:
                // An aggregate rvalue (e.g. a struct returned by a call) is referred to by the
                // CPtr its value already is.
                if (_model.TypeOf(e)?.Type.Type is ArrayType or RecordType)
                    return EmitValue(e);
                throw new EmitException($"expression '{e.GetType().Name}' is not an addressable lvalue");
        }
    }

    private string EmitElementAddress(IndexExpression idx)
    {
        var (basePtr, elem, indexExpr) = ResolveSubscript(idx);
        return $"({basePtr} + {EmitValue(indexExpr)} * {MemoryAccess.SizeOf(elem)})";
    }

    private (string BasePtr, CType Element, Expression Index) ResolveSubscript(IndexExpression idx)
    {
        if (PointeeOf(idx.Array) is { } ea) return (EmitValue(idx.Array), ea, idx.Index);
        if (PointeeOf(idx.Index) is { } eb) return (EmitValue(idx.Index), eb, idx.Array); // i[a] form
        throw new EmitException("subscripted value is neither array nor pointer");
    }

    private string EmitMemberAddress(MemberExpression m)
    {
        RecordType? record;
        string baseAddr;
        if (m.Arrow)
        {
            record = (_model.TypeOf(m.Target)?.Type.Type as PointerType)?.Pointee.Type as RecordType;
            baseAddr = EmitValue(m.Target);
        }
        else
        {
            record = _model.TypeOf(m.Target)?.Type.Type as RecordType;
            baseAddr = EmitAddress(m.Target);
        }

        var field = record?.FindField(m.Member)
            ?? throw new EmitException($"no member '{m.Member}' to take the address of");
        if (field.BitWidth is not null)
            throw new EmitException($"bit-field member '{m.Member}' is not supported yet");
        return $"({baseAddr} + {field.Offset})";
    }

    private static bool IsMemoryLvalue(Expression e, SemanticModel model, MemoryPlan plan) => e switch
    {
        IdentifierExpression id => model.SymbolsByNode.TryGetValue(id, out var s) && s.Kind != SymbolKind.EnumConstant && plan.IsMemoryBacked(s),
        UnaryExpression { Operator: TokenKind.Star } => true,
        IndexExpression => true,
        MemberExpression => true,
        _ => false,
    };

    // --- binary / conditions ---------------------------------------------

    private string EmitBinaryValue(BinaryExpression b)
    {
        if (IsBooleanOperator(b.Operator))
            return $"({EmitCondition(b)} ? 1 : 0)";

        if (b.Operator is TokenKind.Plus or TokenKind.Minus && TryEmitPointerArithmetic(b, out var ptr))
            return ptr;

        return $"({EmitValue(b.Left)} {TokenSpelling.Spell(b.Operator)} {EmitValue(b.Right)})";
    }

    private bool TryEmitPointerArithmetic(BinaryExpression b, out string result)
    {
        // An operand is "indirect" (pointer-like) if it is a pointer or an array (which decays).
        CType? le = PointeeOf(b.Left), re = PointeeOf(b.Right);
        result = string.Empty;

        if (b.Operator == TokenKind.Plus)
        {
            if (le is not null) { result = $"({EmitValue(b.Left)} + {EmitValue(b.Right)} * {MemoryAccess.SizeOf(le)})"; return true; }
            if (re is not null) { result = $"({EmitValue(b.Right)} + {EmitValue(b.Left)} * {MemoryAccess.SizeOf(re)})"; return true; }
            return false;
        }

        // subtraction
        if (le is not null && re is not null)
        {
            result = $"(CPtr.ByteDistance({EmitValue(b.Left)}, {EmitValue(b.Right)}) / {MemoryAccess.SizeOf(le)})";
            return true;
        }
        if (le is not null)
        {
            result = $"({EmitValue(b.Left)} - {EmitValue(b.Right)} * {MemoryAccess.SizeOf(le)})";
            return true;
        }
        return false;
    }

    private CType? PointeeOf(Expression e) => _model.TypeOf(e)?.Type.Type switch
    {
        PointerType p => p.Pointee.Type,
        ArrayType a => a.Element.Type,
        _ => null,
    };

    private static bool IsRelational(TokenKind op) => op is
        TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual
        or TokenKind.EqualEqual or TokenKind.BangEqual;

    private static bool IsBooleanOperator(TokenKind op) => IsRelational(op) || op is TokenKind.AmpAmp or TokenKind.PipePipe;

    /// <summary>Emits an expression as a C# <c>bool</c>.</summary>
    private string EmitCondition(Expression e)
    {
        switch (e)
        {
            case BinaryExpression b when IsRelational(b.Operator):
                return $"({EmitComparand(b.Left, b.Right)} {TokenSpelling.Spell(b.Operator)} {EmitComparand(b.Right, b.Left)})";
            case BinaryExpression { Operator: TokenKind.AmpAmp } b:
                return $"({EmitCondition(b.Left)} && {EmitCondition(b.Right)})";
            case BinaryExpression { Operator: TokenKind.PipePipe } b:
                return $"({EmitCondition(b.Left)} || {EmitCondition(b.Right)})";
            case UnaryExpression { Operator: TokenKind.Bang } u:
                return $"!({EmitCondition(u.Operand)})";
            default:
                var dt = _model.TypeOf(e)?.Type.Type;
                if (TypeMapper.IsFunctionPointer(dt ?? CTypes.Int)) return $"({EmitValue(e)} != null)";
                return dt is PointerType ? $"({EmitValue(e)} != CPtr.Null)" : $"({EmitValue(e)} != 0)";
        }
    }

    /// <summary>Emits a comparison operand, rendering a null-pointer constant to match the sibling's pointer kind.</summary>
    private string EmitComparand(Expression operand, Expression sibling)
    {
        var siblingType = _model.TypeOf(sibling)?.Type.Type;
        if (IsNullConstant(operand) && siblingType is PointerType)
            return TypeMapper.IsFunctionPointer(siblingType) ? "null" : "CPtr.Null";
        return EmitValue(operand);
    }

    // --- assignment -------------------------------------------------------

    private string EmitAssignmentValue(AssignmentExpression a)
    {
        string store = EmitStore(a);
        // A bit-field/memory store is a method call (already a value); the bit-field store returns
        // a long, so cast it to the field type. A plain-local assignment needs parens as a value.
        if (AsBitField(a.Target) is { } bf)
            return $"unchecked(({TypeMapper.Map(bf.FieldType)})({store}))";
        return IsMemoryLvalue(a.Target, _model, _plan) ? store : $"({store})";
    }

    /// <summary>Builds the store expression for an assignment (used for both value and statement contexts).</summary>
    private string EmitStore(AssignmentExpression a)
    {
        var targetType = _model.TypeOf(a.Target)?.Type ?? new QualType(CTypes.Int);

        if (AsBitField(a.Target) is { } bf)
        {
            string rhs = a.Operator == TokenKind.Equal
                ? EmitConvert(new QualType(bf.FieldType), a.Value)
                : CompoundRhs($"__mem.ReadBitField({bf.UnitAddr}, {bf.UnitSize}, {bf.BitOffset}, {bf.BitWidth}, {Bool(bf.Signed)})", bf.FieldType, a.Operator, a.Value);
            return $"__mem.StoreBitField({bf.UnitAddr}, {bf.UnitSize}, {bf.BitOffset}, {bf.BitWidth}, {Bool(bf.Signed)}, {rhs})";
        }

        if (IsMemoryLvalue(a.Target, _model, _plan))
        {
            string addr = EmitAddress(a.Target);
            if (targetType.Type is RecordType r)
                return $"__mem.Copy({addr}, {EmitValue(a.Value)}, {MemoryAccess.SizeOf(r)})"; // struct copy

            string suffix = MemoryAccess.Suffix(targetType.Type);
            string rhs = a.Operator == TokenKind.Equal
                ? EmitConvert(targetType, a.Value)
                : CompoundRhs($"__mem.Read{suffix}({addr})", targetType.Type, a.Operator, a.Value);
            return $"__mem.Store{suffix}({addr}, {rhs})";
        }

        // Plain local / scalar global.
        string target = EmitValue(a.Target);
        if (a.Operator == TokenKind.Equal)
            return $"{target} = {EmitConvert(targetType, a.Value)}";
        if (targetType.Type is PointerType) // pointer += / -= integer
            return $"{target} = {CompoundRhs(target, targetType.Type, a.Operator, a.Value)}";
        return $"{target} {TokenSpelling.Spell(a.Operator)} {EmitValue(a.Value)}"; // C# compound handles the conversion
    }

    /// <summary>The right-hand side of a compound assignment as a plain binary expression (scaling pointer steps).</summary>
    private string CompoundRhs(string current, CType targetType, TokenKind compoundOp, Expression value)
    {
        char op = compoundOp switch
        {
            TokenKind.PlusEqual => '+', TokenKind.MinusEqual => '-', TokenKind.StarEqual => '*',
            TokenKind.SlashEqual => '/', TokenKind.PercentEqual => '%', TokenKind.AmpEqual => '&',
            TokenKind.PipeEqual => '|', TokenKind.CaretEqual => '^',
            _ => throw new EmitException($"compound operator '{TokenSpelling.Spell(compoundOp)}' is not supported yet"),
        };
        if (targetType is PointerType p)
            return $"({current} {op} {EmitValue(value)} * {MemoryAccess.SizeOf(p.Pointee.Type)})";
        return $"unchecked(({TypeMapper.Map(targetType)})({current} {op} {EmitValue(value)}))";
    }

    // --- increment / decrement -------------------------------------------

    private string EmitIncDecValue(Expression operand, TokenKind op, bool isPostfix)
    {
        var type = _model.TypeOf(operand)?.Type.Type ?? CTypes.Int;
        string sign = op == TokenKind.PlusPlus ? "+" : "-";
        long delta = type is PointerType p ? MemoryAccess.SizeOf(p.Pointee.Type) : 1;

        if (AsBitField(operand) is { } bf)
        {
            if (isPostfix)
                throw new EmitException("postfix ++/-- on a bit-field in a value context is not supported yet");
            string read = $"__mem.ReadBitField({bf.UnitAddr}, {bf.UnitSize}, {bf.BitOffset}, {bf.BitWidth}, {Bool(bf.Signed)})";
            string next = $"unchecked(({TypeMapper.Map(bf.FieldType)})({read} {sign} {delta}))";
            return $"unchecked(({TypeMapper.Map(bf.FieldType)})__mem.StoreBitField({bf.UnitAddr}, {bf.UnitSize}, {bf.BitOffset}, {bf.BitWidth}, {Bool(bf.Signed)}, {next}))";
        }

        if (IsMemoryLvalue(operand, _model, _plan))
        {
            string addr = EmitAddress(operand);
            string suffix = MemoryAccess.Suffix(type);
            if (isPostfix)
                throw new EmitException("postfix ++/-- on a memory object in a value context is not supported yet");
            string updated = type is PointerType
                ? $"(__mem.Read{suffix}({addr}) {sign} {delta})"
                : $"unchecked(({TypeMapper.Map(type)})(__mem.Read{suffix}({addr}) {sign} {delta}))";
            return $"__mem.Store{suffix}({addr}, {updated})";
        }

        string name = EmitValue(operand);
        if (type is PointerType)
            return isPostfix ? $"__PostAddPtr(ref {name}, {sign}{delta})" : $"({name} = {name} + ({sign}{delta}))";
        return isPostfix ? $"({name}{TokenSpelling.Spell(op)})" : $"({TokenSpelling.Spell(op)}{name})";
    }

    // --- calls (user + libc shims) ---------------------------------------

    /// <summary>Emits a call in value position, marshalling an export's CLR return type back to the C type.</summary>
    private string EmitCallValue(CallExpression call)
    {
        string raw = EmitCall(call);
        if (ResolveExport(call) is not { } export)
            return raw;

        switch (export.ReturnClrType)
        {
            case "string": // managed string -> C char*
                return $"AnsiCCore.Runtime.Library.CMarshal.FromManaged(__mem, {raw})";
            case "bool":   // C# bool -> C int 0/1
                return $"({raw} ? 1 : 0)";
        }

        if (export.ReturnsInteger
            && _model.TypeOf(call)?.Type.Type is { } rt && rt is IntegerType or EnumType
            && TypeMapper.Map(rt) != export.ReturnClrType)
            return $"unchecked(({TypeMapper.Map(rt)})({raw}))"; // reconcile e.g. strlen's int with size_t
        return raw;
    }

    /// <summary>Emits the bare call expression: a linked export (via its attribute binding) or a user call.</summary>
    private string EmitCall(CallExpression call)
    {
        if (ResolveExport(call) is { } export)
            return EmitExportedCall(call, export);

        var fnType = _model.TypeOf(call.Callee)?.Type.Type as FunctionType;
        var args = call.Arguments.Select((arg, i) =>
            fnType is { HasPrototype: true } && i < fnType.Parameters.Count
                ? EmitConvert(fnType.Parameters[i].Type, arg)
                : EmitValue(arg));
        return $"{EmitValue(call.Callee)}({string.Join(", ", args)})";
    }

    /// <summary>Resolves a call to a linked export, unless the program defines the function itself.</summary>
    private ExportedFunction? ResolveExport(CallExpression call)
        => call.Callee is IdentifierExpression callee && !_userFunctions.Contains(callee.Name)
            ? _exports.Resolve(callee.Name)
            : null;

    /// <summary>Marshals the C arguments onto the exported method's parameters and emits the call.</summary>
    private string EmitExportedCall(CallExpression call, ExportedFunction export)
    {
        var parts = new List<string>();
        if (export.TakesMemory)
            parts.Add("__mem");

        int fixedCount = export.ParameterClrTypes.Count;
        for (int i = 0; i < fixedCount && i < call.Arguments.Count; i++)
            parts.Add(MarshalArgument(export.ParameterClrTypes[i], call.Arguments[i]));

        if (export.IsVariadic)
        {
            var rest = call.Arguments.Skip(fixedCount).Select(a => $"(object)({EmitValue(a)})");
            parts.Add($"new object[] {{ {string.Join(", ", rest)} }}");
        }

        return $"{export.DeclaringTypeFullName}.{export.MethodName}({string.Join(", ", parts)})";
    }

    /// <summary>Converts one C argument to an exported method parameter's CLR type.</summary>
    private string MarshalArgument(string parameterClrType, Expression arg)
    {
        switch (parameterClrType)
        {
            case not null when parameterClrType.EndsWith("CPtr", StringComparison.Ordinal):
                return IsNullConstant(arg) ? "CPtr.Null" : EmitValue(arg);
            case "string": // C char* -> managed string
                return $"AnsiCCore.Runtime.Library.CMarshal.ToManaged(__mem, {EmitValue(arg)})";
            case "bool":   // C int -> C# bool
                return EmitCondition(arg);
        }

        string value = EmitValue(arg);
        string? argClr = _model.TypeOf(arg)?.Type is { } t ? TypeMapper.Map(t) : null;
        return argClr == parameterClrType ? value : $"unchecked(({parameterClrType})({value}))";
    }

    private string EmitCast(CastExpression cast)
    {
        var target = _model.ResolvedTypeNames.GetValueOrDefault(cast.Type);
        var source = _model.TypeOf(cast.Operand)?.Type.Type;

        return target.Type switch
        {
            null => throw new EmitException("cast target type was not resolved"),
            VoidType => throw new EmitException("cast to void is not supported yet"),
            PointerType when IsNullConstant(cast.Operand) => "CPtr.Null",
            PointerType when source is PointerType or ArrayType => EmitValue(cast.Operand),
            PointerType => $"new CPtr(unchecked((ulong)({EmitValue(cast.Operand)})))", // integer -> pointer
            _ when source is PointerType => $"unchecked(({TypeMapper.Map(target)})({EmitValue(cast.Operand)}).Address)", // pointer -> integer
            _ => $"unchecked(({TypeMapper.Map(target)})({EmitValue(cast.Operand)}))",
        };
    }

    private string EmitSizeof(CType? operandType)
    {
        long? size = operandType is null ? null : TypeOperations.SizeOf(operandType);
        return size is null
            ? throw new EmitException("sizeof of an unsupported or incomplete type")
            : $"{size.Value}UL";
    }

    // --- conversions ------------------------------------------------------

    private string EmitConvert(QualType target, Expression e)
    {
        // A by-value struct/union argument or return value is copied into fresh storage.
        if (target.Type is RecordType r)
            return $"__ByValue({EmitValue(e)}, {MemoryAccess.SizeOf(r)})";

        if (TypeMapper.IsFunctionPointer(target.Type))
            return IsNullConstant(e) ? "null" : EmitValue(e); // function pointers are delegates

        if (target.Type is PointerType)
            return IsNullConstant(e) ? "CPtr.Null" : EmitValue(e);

        string value = EmitValue(e);
        var source = _model.TypeOf(e)?.Type;
        if (source is null || TypeMapper.IsFunctionPointer(source.Value.Type))
            return value;
        if (source.Value.Type is PointerType) // pointer -> integer (implicit, as in a return/assignment)
            return $"unchecked(({TypeMapper.Map(target)})({value}).Address)";
        return TypeMapper.Map(source.Value) == TypeMapper.Map(target) ? value : $"unchecked(({TypeMapper.Map(target)})({value}))";
    }

    private bool IsNullConstant(Expression e)
        => e is IntegerLiteral i && DecodeInteger(i.Text) == 0;

    // --- statement-expression / for-clause forms -------------------------

    private string EmitStatementExpression(Expression e)
    {
        switch (e)
        {
            case AssignmentExpression a:
                return EmitStore(a); // bare: either "L = v" or a "__mem.StoreX(...)" call, both valid statements
            case CallExpression call:
                return EmitCall(call); // bare call (value discarded); avoids an invalid cast-expression statement
            case PostfixExpression p:
                return EmitIncDecStatement(p.Operand, p.Operator);
            case UnaryExpression { Operator: TokenKind.PlusPlus or TokenKind.MinusMinus } u:
                return EmitIncDecStatement(u.Operand, u.Operator);
            default:
                return $"_ = {EmitValue(e)}";
        }
    }

    /// <summary>Statement-form ++/--: performs the update without needing to yield a value.</summary>
    private string EmitIncDecStatement(Expression operand, TokenKind op)
    {
        var type = _model.TypeOf(operand)?.Type.Type ?? CTypes.Int;
        string sign = op == TokenKind.PlusPlus ? "+" : "-";
        long delta = type is PointerType p ? MemoryAccess.SizeOf(p.Pointee.Type) : 1;

        if (AsBitField(operand) is { } bf)
        {
            string read = $"__mem.ReadBitField({bf.UnitAddr}, {bf.UnitSize}, {bf.BitOffset}, {bf.BitWidth}, {Bool(bf.Signed)})";
            string next = $"unchecked(({TypeMapper.Map(bf.FieldType)})({read} {sign} {delta}))";
            return $"__mem.StoreBitField({bf.UnitAddr}, {bf.UnitSize}, {bf.BitOffset}, {bf.BitWidth}, {Bool(bf.Signed)}, {next})";
        }

        if (IsMemoryLvalue(operand, _model, _plan))
        {
            string addr = EmitAddress(operand);
            string suffix = MemoryAccess.Suffix(type);
            string updated = type is PointerType
                ? $"(__mem.Read{suffix}({addr}) {sign} {delta})"
                : $"unchecked(({TypeMapper.Map(type)})(__mem.Read{suffix}({addr}) {sign} {delta}))";
            return $"__mem.Write{suffix}({addr}, {updated})";
        }

        string name = EmitValue(operand);
        if (type is PointerType)
            return $"{name} = {name} + ({sign}{delta})";
        return $"{name}{TokenSpelling.Spell(op)}";
    }

    private string EmitForClause(Expression e)
        => e is CommaExpression c ? $"{EmitForClause(c.Left)}, {EmitForClause(c.Right)}" : EmitStatementExpression(e);

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
        return last is 'f' or 'F' ? body + "f" : body;
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
        var bytes = DecodeCharsBetween(text, i, end);
        return bytes.Count > 0 ? bytes[^1] : 0;
    }

    private static byte[] DecodeStringBytes(StringLiteral s)
    {
        var bytes = new List<byte>();
        foreach (var part in s.Parts)
        {
            int i = 0;
            if (i < part.Length && part[i] == 'L') i++;
            if (i < part.Length && part[i] == '"') i++;
            int end = part.EndsWith('"') ? part.Length - 1 : part.Length;
            bytes.AddRange(DecodeCharsBetween(part, i, end).Select(v => (byte)v));
        }
        bytes.Add(0); // NUL terminator
        return [.. bytes];
    }

    /// <summary>Decodes the character/escape sequence content of a literal body into byte values.</summary>
    private static List<int> DecodeCharsBetween(string text, int start, int end)
    {
        var result = new List<int>();
        int i = start;
        while (i < end)
        {
            char c = text[i++];
            if (c != '\\') { result.Add(c); continue; }
            if (i >= end) { result.Add('\\'); break; }
            char esc = text[i++];
            switch (esc)
            {
                case 'n': result.Add('\n'); break;
                case 't': result.Add('\t'); break;
                case 'r': result.Add('\r'); break;
                case 'v': result.Add('\v'); break;
                case 'f': result.Add('\f'); break;
                case 'b': result.Add('\b'); break;
                case 'a': result.Add('\a'); break;
                case '\\': result.Add('\\'); break;
                case '\'': result.Add('\''); break;
                case '"': result.Add('"'); break;
                case '?': result.Add('?'); break;
                case >= '0' and <= '7':
                {
                    int v = esc - '0';
                    for (int k = 0; k < 2 && i < end && text[i] is >= '0' and <= '7'; k++) v = v * 8 + (text[i++] - '0');
                    result.Add(v & 0xFF);
                    break;
                }
                case 'x':
                {
                    int v = 0;
                    while (i < end && Uri.IsHexDigit(text[i])) v = v * 16 + Convert.ToInt32(text[i++].ToString(), 16);
                    result.Add(v & 0xFF);
                    break;
                }
                default: result.Add(esc); break;
            }
        }
        return result;
    }
}
