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
        if (d.Specifiers.IsTypedef) return; // local typedef is compile-time only

        foreach (var init in d.InitDeclarators)
        {
            if (!_model.SymbolsByNode.TryGetValue(init, out var symbol))
                continue;
            if (symbol.Kind is SymbolKind.Typedef or SymbolKind.Function)
                continue;

            string type = TypeMapper.Map(symbol.Type);
            string name = DeclareLocalName(symbol);
            // C locals are indeterminate; C# requires definite assignment, so default-initialize.
            string value = init.Initializer is ExpressionInitializer ei
                ? EmitConvert(symbol.Type, ei.Expression)
                : "default";
            Line($"{type} {name} = {value};");
        }
    }

    private void EmitStatement(Statement statement)
    {
        switch (statement)
        {
            case CompoundStatement c:
                EmitBlock(c);
                break;

            case ExpressionStatement { Expression: null }:
                Line(";");
                break;

            case ExpressionStatement e:
                EmitExpressionStatement(e.Expression!);
                break;

            case ReturnStatement r:
                EmitReturn(r);
                break;

            case IfStatement i:
                Line($"if ({EmitCondition(i.Condition)})");
                EmitNested(i.Then);
                if (i.Otherwise is not null)
                {
                    Line("else");
                    EmitNested(i.Otherwise);
                }
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

            case ForStatement f:
                EmitFor(f);
                break;

            case BreakStatement:
                Line("break;");
                break;

            case ContinueStatement:
                Line("continue;");
                break;

            default:
                throw new EmitException($"statement '{statement.GetType().Name}' is not supported yet");
        }
    }

    /// <summary>Emits a statement as a nested body — a block as-is, anything else indented one level.</summary>
    private void EmitNested(Statement statement)
    {
        if (statement is CompoundStatement c)
        {
            EmitBlock(c);
            return;
        }
        _indent++;
        EmitStatement(statement);
        _indent--;
    }

    private void EmitReturn(ReturnStatement r)
    {
        if (r.Value is null)
        {
            Line("return;");
            return;
        }
        string value = _currentReturnType is { } rt && rt.Type is not VoidType
            ? EmitConvert(rt, r.Value)
            : EmitValue(r.Value);
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

    /// <summary>Emits a C expression statement, splitting a top-level comma into separate statements.</summary>
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
