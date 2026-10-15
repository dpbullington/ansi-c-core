using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Lexing;
using AnsiCCore.Semantics.Symbols;
using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Emit;

/// <summary>
/// Decides which objects must live in <c>CMemory</c> (have an address) versus being plain C#
/// locals/fields. An object is memory-backed when it is an aggregate (array/struct/union) or
/// its address is taken anywhere via <c>&amp;</c>. Everything else — scalars and pointers whose
/// address is never taken — stays a value, which keeps the common case fast and readable.
/// </summary>
internal sealed class MemoryPlan
{
    private readonly HashSet<Symbol> _addressTaken;
    private readonly SemanticModel _model;

    private MemoryPlan(HashSet<Symbol> addressTaken, SemanticModel model)
    {
        _addressTaken = addressTaken;
        _model = model;
    }

    public static MemoryPlan Build(TranslationUnit unit, SemanticModel model)
    {
        var taken = new HashSet<Symbol>();
        foreach (var item in unit.Items)
        {
            switch (item)
            {
                case FunctionDefinition f: CollectStatement(f.Body, model, taken); break;
                case Declaration d: CollectDeclaration(d, model, taken); break;
            }
        }
        return new MemoryPlan(taken, model);
    }

    public bool IsMemoryBacked(Symbol symbol)
        => symbol.Type.Type is ArrayType or RecordType || _addressTaken.Contains(symbol);

    // --- address-of collection -------------------------------------------

    private static void CollectStatement(Statement? s, SemanticModel model, HashSet<Symbol> taken)
    {
        switch (s)
        {
            case null: break;
            case CompoundStatement c:
                foreach (var item in c.Items)
                {
                    if (item is Declaration d) CollectDeclaration(d, model, taken);
                    else if (item is Statement inner) CollectStatement(inner, model, taken);
                }
                break;
            case ExpressionStatement e: CollectExpression(e.Expression, model, taken); break;
            case IfStatement i: CollectExpression(i.Condition, model, taken); CollectStatement(i.Then, model, taken); CollectStatement(i.Otherwise, model, taken); break;
            case WhileStatement w: CollectExpression(w.Condition, model, taken); CollectStatement(w.Body, model, taken); break;
            case DoStatement dd: CollectStatement(dd.Body, model, taken); CollectExpression(dd.Condition, model, taken); break;
            case ForStatement f: CollectExpression(f.Initializer, model, taken); CollectExpression(f.Condition, model, taken); CollectExpression(f.Iteration, model, taken); CollectStatement(f.Body, model, taken); break;
            case SwitchStatement sw: CollectExpression(sw.Expression, model, taken); CollectStatement(sw.Body, model, taken); break;
            case CaseStatement cs: CollectExpression(cs.Value, model, taken); CollectStatement(cs.Statement, model, taken); break;
            case DefaultStatement df: CollectStatement(df.Statement, model, taken); break;
            case LabeledStatement l: CollectStatement(l.Statement, model, taken); break;
            case ReturnStatement r: CollectExpression(r.Value, model, taken); break;
        }
    }

    private static void CollectDeclaration(Declaration d, SemanticModel model, HashSet<Symbol> taken)
    {
        foreach (var init in d.InitDeclarators)
            if (init.Initializer is not null)
                CollectInitializer(init.Initializer, model, taken);
    }

    private static void CollectInitializer(Initializer init, SemanticModel model, HashSet<Symbol> taken)
    {
        switch (init)
        {
            case ExpressionInitializer e: CollectExpression(e.Expression, model, taken); break;
            case InitializerList l: foreach (var item in l.Items) CollectInitializer(item, model, taken); break;
        }
    }

    private static void CollectExpression(Expression? e, SemanticModel model, HashSet<Symbol> taken)
    {
        switch (e)
        {
            case null: break;
            case UnaryExpression { Operator: TokenKind.Ampersand } u:
                if (u.Operand is IdentifierExpression id && model.SymbolsByNode.TryGetValue(id, out var sym))
                    taken.Add(sym);
                CollectExpression(u.Operand, model, taken);
                break;
            case UnaryExpression u: CollectExpression(u.Operand, model, taken); break;
            case PostfixExpression p: CollectExpression(p.Operand, model, taken); break;
            case BinaryExpression b: CollectExpression(b.Left, model, taken); CollectExpression(b.Right, model, taken); break;
            case AssignmentExpression a: CollectExpression(a.Target, model, taken); CollectExpression(a.Value, model, taken); break;
            case ConditionalExpression c: CollectExpression(c.Condition, model, taken); CollectExpression(c.WhenTrue, model, taken); CollectExpression(c.WhenFalse, model, taken); break;
            case CommaExpression cm: CollectExpression(cm.Left, model, taken); CollectExpression(cm.Right, model, taken); break;
            case CallExpression call: CollectExpression(call.Callee, model, taken); foreach (var arg in call.Arguments) CollectExpression(arg, model, taken); break;
            case IndexExpression idx: CollectExpression(idx.Array, model, taken); CollectExpression(idx.Index, model, taken); break;
            case MemberExpression m: CollectExpression(m.Target, model, taken); break;
            case SizeOfExpression so: CollectExpression(so.Operand, model, taken); break;
            case CastExpression cast: CollectExpression(cast.Operand, model, taken); break;
        }
    }
}
