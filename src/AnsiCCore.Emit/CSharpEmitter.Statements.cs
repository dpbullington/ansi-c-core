using AnsiCCore.Frontend.Ast;
using AnsiCCore.Semantics.Symbols;
using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Emit;

public sealed partial class CSharpEmitter
{
    private void EmitBlock(CompoundStatement block)
    {
        OpenBrace();
        foreach (var item in block.Items)
            EmitBlockItem(item);
        CloseBrace();
    }

    private void EmitBlockItem(Node item)
    {
        switch (item)
        {
            case Declaration d: EmitLocalDeclaration(d); break;
            case Statement s: EmitStatement(s); break;
        }
    }

    private void EmitLocalDeclaration(Declaration d)
    {
        if (d.Specifiers.IsTypedef) return;

        foreach (var init in d.InitDeclarators)
        {
            if (!_model.SymbolsByNode.TryGetValue(init, out var symbol)) continue;
            if (symbol.Kind is SymbolKind.Typedef or SymbolKind.Function) continue;

            var type = symbol.Type;
            string name = DeclareLocalName(symbol);

            if (_plan.IsMemoryBacked(symbol))
            {
                _addressOf[symbol] = name;
                long size = AllocationSize(type.Type, init.Initializer);
                Line($"CPtr {name} = __mem.Allocate({size}, RegionKind.Automatic);");
                if (init.Initializer is not null)
                    EmitInitializer(type.Type, name, init.Initializer);
            }
            else
            {
                // C locals are indeterminate; C# requires definite assignment, so default-initialize.
                string value = init.Initializer is ExpressionInitializer ei ? EmitConvert(type, ei.Expression) : "default";
                Line($"{TypeMapper.Map(type)} {name} = {value};");
            }
        }
    }

    /// <summary>The number of bytes to allocate, deriving an unspecified array bound from the initializer.</summary>
    private long AllocationSize(CType type, Initializer? initializer)
    {
        if (TypeOperations.SizeOf(type) is { } known) return known;

        if (type is ArrayType a)
        {
            if (initializer is ExpressionInitializer { Expression: StringLiteral sl })
                return DecodeStringBytes(sl).Length;
            if (initializer is InitializerList l)
                return l.Items.Count * MemoryAccess.SizeOf(a.Element.Type);
        }
        throw new EmitException($"cannot determine allocation size for '{type.Display}'");
    }

    /// <summary>Writes an initializer into memory at <paramref name="addr"/> (a CPtr-valued C# expression).</summary>
    private void EmitInitializer(CType type, string addr, Initializer initializer)
    {
        switch (type)
        {
            case ArrayType a when initializer is ExpressionInitializer { Expression: StringLiteral sl }:
            {
                string field = InternString(DecodeStringBytes(sl));
                Line($"__mem.Copy({addr}, {field}, {DecodeStringBytes(sl).Length});");
                break;
            }
            case ArrayType a when initializer is InitializerList list:
            {
                long esize = MemoryAccess.SizeOf(a.Element.Type);
                for (int i = 0; i < list.Items.Count; i++)
                    EmitInitializer(a.Element.Type, $"({addr} + {i * esize})", list.Items[i]);
                break;
            }
            case RecordType r when initializer is InitializerList list:
            {
                for (int i = 0; i < list.Items.Count && i < r.Fields.Count; i++)
                    EmitInitializer(r.Fields[i].Type.Type, $"({addr} + {r.Fields[i].Offset})", list.Items[i]);
                break;
            }
            case RecordType r when initializer is ExpressionInitializer e:
                Line($"__mem.Copy({addr}, {EmitValue(e.Expression)}, {MemoryAccess.SizeOf(r)});");
                break;
            default:
                if (initializer is ExpressionInitializer scalar)
                    Line($"__mem.Write{MemoryAccess.Suffix(type)}({addr}, {EmitConvert(new QualType(type), scalar.Expression)});");
                else
                    throw new EmitException($"unsupported initializer for '{type.Display}'");
                break;
        }
    }

    private void EmitStatement(Statement statement)
    {
        switch (statement)
        {
            case CompoundStatement c: EmitBlock(c); break;
            case ExpressionStatement { Expression: null }: Line(";"); break;
            case ExpressionStatement e: EmitExpressionStatement(e.Expression!); break;
            case ReturnStatement r: EmitReturn(r); break;

            case IfStatement i:
                Line($"if ({EmitCondition(i.Condition)})");
                EmitNested(i.Then);
                if (i.Otherwise is not null) { Line("else"); EmitNested(i.Otherwise); }
                break;

            case WhileStatement w:
                Line($"while ({EmitCondition(w.Condition)})");
                EmitNested(w.Body);
                break;

            case DoStatement d:
                Line("do");
                EmitNested(d.Body);
                Line($"while ({EmitCondition(d.Condition)});");
                break;

            case ForStatement f: EmitFor(f); break;

            case BreakStatement: Line("break;"); break;
            case ContinueStatement: Line("continue;"); break;

            default:
                throw new EmitException($"statement '{statement.GetType().Name}' is not supported yet");
        }
    }

    private void EmitNested(Statement statement)
    {
        if (statement is CompoundStatement c) { EmitBlock(c); return; }
        _indent++;
        EmitStatement(statement);
        _indent--;
    }

    private void EmitReturn(ReturnStatement r)
    {
        if (r.Value is null) { Line("return;"); return; }
        string value = _currentReturnType is { Type: not VoidType } rt ? EmitConvert(rt, r.Value) : EmitValue(r.Value);
        Line($"return {value};");
    }

    private void EmitFor(ForStatement f)
    {
        string init = f.Initializer is null ? string.Empty : EmitForClause(f.Initializer);
        string cond = f.Condition is null ? string.Empty : EmitCondition(f.Condition);
        string iter = f.Iteration is null ? string.Empty : EmitForClause(f.Iteration);
        Line($"for ({init}; {cond}; {iter})");
        EmitNested(f.Body);
    }

    private void EmitExpressionStatement(Expression e)
    {
        if (e is CommaExpression c)
        {
            EmitExpressionStatement(c.Left);
            EmitExpressionStatement(c.Right);
            return;
        }
        Line($"{EmitStatementExpression(e)};");
    }
}
