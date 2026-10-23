using AnsiCCore.Frontend.Ast;
using AnsiCCore.Semantics.Symbols;
using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Emit;

public sealed partial class CSharpEmitter
{
    private void EmitBlock(CompoundStatement block)
    {
        OpenBrace();
        EmitBlockItems(block.Items);
        CloseBrace();
    }

    /// <summary>Emits a block's items, wrapping the tail from the first setjmp occurrence onward in a
    /// retry region (see EmitSetjmpGuard) so a longjmp re-enters it.</summary>
    private void EmitBlockItems(IReadOnlyList<Node> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (ItemSetjmpCall(items[i]) is { } sjCall)
            {
                EmitSetjmpGuard(items, i, sjCall);
                return; // the guard emitted every remaining item
            }
            EmitBlockItem(items[i]);
        }
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
                _breakStack.Push(null);
                EmitNested(w.Body);
                _breakStack.Pop();
                break;

            case DoStatement d:
                Line("do");
                _breakStack.Push(null);
                EmitNested(d.Body);
                _breakStack.Pop();
                Line($"while ({EmitCondition(d.Condition)});");
                break;

            case ForStatement f: EmitFor(f); break;
            case SwitchStatement sw: EmitSwitch(sw); break;

            case CaseStatement c: EmitCaseLabel(c, _switchCaseLabels?.GetValueOrDefault(c), c.Statement); break;
            case DefaultStatement df: EmitCaseLabel(df, _switchCaseLabels?.GetValueOrDefault(df), df.Statement); break;

            case LabeledStatement l:
                Line($"{Naming.Safe(l.Label)}:;");
                EmitStatement(l.Statement);
                break;

            case GotoStatement g:
                Line($"goto {Naming.Safe(g.Label)};");
                break;

            case BreakStatement:
                Line(_breakStack.Count > 0 && _breakStack.Peek() is { } label ? $"goto {label};" : "break;");
                break;

            case ContinueStatement:
                Line("continue;");
                break;

            default:
                throw new EmitException($"statement '{statement.GetType().Name}' is not supported yet");
        }
    }

    // --- setjmp / longjmp -------------------------------------------------

    private readonly Dictionary<CallExpression, string> _setjmpVars = new();
    private int _setjmpCounter;

    /// <summary>Finds a <c>setjmp(...)</c> call reachable in the expressions a block item evaluates
    /// directly (declaration initializers, an expression/return statement, or a controlling
    /// expression of an if/switch/while/do/for); otherwise null. The block from this item onward is
    /// then wrapped so a longjmp re-enters it.</summary>
    private static CallExpression? ItemSetjmpCall(Node item) => item switch
    {
        Declaration d => d.InitDeclarators.Select(x => x.Initializer).OfType<ExpressionInitializer>()
            .Select(x => FindSetjmpCall(x.Expression)).FirstOrDefault(c => c is not null),
        ExpressionStatement { Expression: { } e } => FindSetjmpCall(e),
        ReturnStatement { Value: { } v } => FindSetjmpCall(v),
        IfStatement i => FindSetjmpCall(i.Condition),
        SwitchStatement s => FindSetjmpCall(s.Expression),
        WhileStatement w => FindSetjmpCall(w.Condition),
        DoStatement do_ => FindSetjmpCall(do_.Condition),
        ForStatement f => FindSetjmpCall(f.Initializer) ?? FindSetjmpCall(f.Condition) ?? FindSetjmpCall(f.Iteration),
        _ => null,
    };

    /// <summary>Recursively finds a <c>setjmp(...)</c> call anywhere within an expression.</summary>
    private static CallExpression? FindSetjmpCall(Expression? e) => e switch
    {
        null => null,
        CallExpression { Callee: IdentifierExpression { Name: "setjmp" } } c => c,
        CallExpression c => FindSetjmpCall(c.Callee) ?? c.Arguments.Select(FindSetjmpCall).FirstOrDefault(x => x is not null),
        BinaryExpression b => FindSetjmpCall(b.Left) ?? FindSetjmpCall(b.Right),
        AssignmentExpression a => FindSetjmpCall(a.Target) ?? FindSetjmpCall(a.Value),
        ConditionalExpression c => FindSetjmpCall(c.Condition) ?? FindSetjmpCall(c.WhenTrue) ?? FindSetjmpCall(c.WhenFalse),
        CommaExpression cm => FindSetjmpCall(cm.Left) ?? FindSetjmpCall(cm.Right),
        UnaryExpression u => FindSetjmpCall(u.Operand),
        PostfixExpression p => FindSetjmpCall(p.Operand),
        CastExpression cast => FindSetjmpCall(cast.Operand),
        IndexExpression ix => FindSetjmpCall(ix.Array) ?? FindSetjmpCall(ix.Index),
        MemberExpression m => FindSetjmpCall(m.Target),
        SizeOfExpression => null, // sizeof does not evaluate its operand
        _ => null,
    };

    /// <summary>
    /// Emits <paramref name="items"/> from <paramref name="start"/> onward inside a goto-based retry
    /// region. On entry the jmp_buf is armed and the setjmp call stands in for a result variable
    /// (0 the first time); a matching longjmp re-enters the region with the thrown value. The region
    /// uses labels rather than a loop so that any C <c>break</c>/<c>continue</c> inside it still
    /// targets the program's own enclosing loop/switch.
    /// </summary>
    private void EmitSetjmpGuard(IReadOnlyList<Node> items, int start, CallExpression sjCall)
    {
        if (sjCall.Arguments.Count != 1)
            throw new EmitException("setjmp expects exactly one argument");

        string env = EmitValue(sjCall.Arguments[0]);
        int n = _setjmpCounter++;
        string sj = $"__sj{n}", tok = $"__sjtok{n}", ex = $"__sje{n}", retry = $"__sjretry{n}", done = $"__sjdone{n}";

        Line($"int {sj} = 0;");
        Line($"long {tok};");
        Line($"{retry}:");
        Line($"{tok} = AnsiCCore.Runtime.Library.CSetjmp.Arm(__mem, {env});");
        Line("try");
        OpenBrace();
        _setjmpVars[sjCall] = sj;
        for (int k = start; k < items.Count; k++) EmitBlockItem(items[k]);
        _setjmpVars.Remove(sjCall);
        Line($"goto {done};");
        CloseBrace();
        Line($"catch (AnsiCCore.Runtime.Library.CLongjmp {ex}) when ({ex}.Token == {tok})");
        OpenBrace();
        Line($"{sj} = {ex}.Value;");
        Line($"goto {retry};");
        CloseBrace();
        Line($"{done}: ;");
    }

    private void EmitCaseLabel(Statement node, string? label, Statement inner)
    {
        if (label is null)
            throw new EmitException("'case'/'default' label outside of a switch");
        Line($"{label}:;");
        EmitStatement(inner);
    }

    /// <summary>
    /// Lowers a C switch to a dispatch of gotos plus a linear body, which preserves fall-through
    /// (C# forbids it) and handles labels interleaved with statements. The common form — cases at
    /// the top of the switch body — is supported; jumping into a nested block (Duff's device) is not.
    /// </summary>
    private void EmitSwitch(SwitchStatement sw)
    {
        int id = _labelCounter++;
        string end = $"__swEnd{id}";
        var items = (sw.Body as CompoundStatement)?.Items ?? [sw.Body];

        var labels = new Dictionary<Statement, string>();
        var cases = new List<CaseStatement>();
        DefaultStatement? defaultNode = null;
        int caseIndex = 0;
        foreach (var item in items)
            if (item is Statement s)
                CollectCases(s, id, labels, cases, ref defaultNode, ref caseIndex);

        OpenBrace();
        Line($"var __sw{id} = {EmitValue(sw.Expression)};");
        foreach (var c in cases)
            Line($"if (__sw{id} == {EmitValue(c.Value)}) goto {labels[c]};");
        Line($"goto {(defaultNode is not null ? labels[defaultNode] : end)};");

        var savedLabels = _switchCaseLabels;
        _switchCaseLabels = labels;
        _breakStack.Push(end);
        foreach (var item in items)
            EmitBlockItem(item);
        _breakStack.Pop();
        _switchCaseLabels = savedLabels;

        Line($"{end}:;");
        CloseBrace();
    }

    /// <summary>Records the label for each case/default, following the chain of consecutive labels.</summary>
    private static void CollectCases(Statement s, int switchId, Dictionary<Statement, string> labels,
        List<CaseStatement> cases, ref DefaultStatement? defaultNode, ref int caseIndex)
    {
        switch (s)
        {
            case CaseStatement c:
                labels[c] = $"__case{switchId}_{caseIndex++}";
                cases.Add(c);
                CollectCases(c.Statement, switchId, labels, cases, ref defaultNode, ref caseIndex);
                break;
            case DefaultStatement d:
                labels[d] = $"__default{switchId}";
                defaultNode = d;
                CollectCases(d.Statement, switchId, labels, cases, ref defaultNode, ref caseIndex);
                break;
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
        _breakStack.Push(null);
        EmitNested(f.Body);
        _breakStack.Pop();
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
