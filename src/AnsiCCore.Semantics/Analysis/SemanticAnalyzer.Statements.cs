using AnsiCCore.Frontend.Ast;
using AnsiCCore.Semantics.Symbols;
using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Semantics.Analysis;

public sealed partial class SemanticAnalyzer
{
    // --- function definitions ---------------------------------------------

    private void AnalyzeFunctionDefinition(FunctionDefinition f)
    {
        var baseType = ResolveSpecifiers(f.Specifiers);
        var storage = ResolveStorage(f.Specifiers);
        var (name, type) = ResolveDeclarator(baseType, f.Declarator);

        if (name is null)
        {
            Error(f.Location, "function definition does not declare a name");
            return;
        }

        var functionType = type.Type as FunctionType;
        if (functionType is null)
            Error(f.Location, $"'{name}' declared as a function definition but is not of function type");

        var symbol = new Symbol(name, SymbolKind.Function, type, storage, f.Location) { IsDefined = true };
        DeclareOrdinaryChecked(symbol, fileScope: true);
        _symbols[f] = symbol;

        // Set up the per-function context.
        _definedLabels.Clear();
        _gotos.Clear();
        _currentFunctionReturn = functionType?.ReturnType ?? baseType;

        // Parameters share the function's top-level block scope.
        var previous = _current;
        _current = new Scope(ScopeKind.Block, _global);
        DeclareParameters(f, functionType);

        foreach (var item in f.Body.Items)
            AnalyzeBlockItem(item);

        _current = previous;

        foreach (var (label, loc) in _gotos)
            if (!_definedLabels.ContainsKey(label))
                Error(loc, $"label '{label}' used but not defined");

        _currentFunctionReturn = null;
    }

    private void DeclareParameters(FunctionDefinition f, FunctionType? functionType)
    {
        var funcDeclarator = FindFunctionDeclarator(f.Declarator);

        if (funcDeclarator?.Parameters.Kind == ParameterKind.IdentifierList)
        {
            // K&R: names come from the identifier list; types from the parameter declarations.
            var types = new Dictionary<string, QualType>(StringComparer.Ordinal);
            foreach (var decl in f.ParameterDeclarations)
            {
                var pBase = ResolveSpecifiers(decl.Specifiers);
                foreach (var init in decl.InitDeclarators)
                {
                    var (pName, pType) = ResolveDeclarator(pBase, init.Declarator);
                    if (pName is not null) types[pName] = AdjustParameterType(pType);
                }
            }

            foreach (var pName in funcDeclarator.Parameters.Identifiers)
            {
                var pType = types.GetValueOrDefault(pName, new QualType(CTypes.Int));
                _current.DeclareOrdinary(new Symbol(pName, SymbolKind.Parameter, pType, StorageClass.None, f.Location));
            }
            return;
        }

        // Prototype: declare each named parameter.
        if (functionType is not null)
        {
            foreach (var p in functionType.Parameters)
                if (p.Name is not null)
                    _current.DeclareOrdinary(new Symbol(p.Name, SymbolKind.Parameter, p.Type, StorageClass.None, f.Location));
        }
    }

    private static FunctionDeclarator? FindFunctionDeclarator(Declarator d) => d switch
    {
        FunctionDeclarator f => f,
        PointerDeclarator p => FindFunctionDeclarator(p.Inner),
        ArrayDeclarator a => FindFunctionDeclarator(a.Inner),
        _ => null,
    };

    // --- statements -------------------------------------------------------

    private void AnalyzeBlockItem(Node item)
    {
        switch (item)
        {
            case Declaration d: AnalyzeDeclaration(d, fileScope: false); break;
            case Statement s: AnalyzeStatement(s); break;
        }
    }

    private void AnalyzeStatement(Statement statement)
    {
        switch (statement)
        {
            case CompoundStatement c:
                EnterScope();
                foreach (var item in c.Items) AnalyzeBlockItem(item);
                ExitScope();
                break;

            case ExpressionStatement e:
                if (e.Expression is not null) AnalyzeExpression(e.Expression);
                break;

            case IfStatement i:
                AnalyzeExpression(i.Condition);
                AnalyzeStatement(i.Then);
                if (i.Otherwise is not null) AnalyzeStatement(i.Otherwise);
                break;

            case WhileStatement w:
                AnalyzeExpression(w.Condition);
                AnalyzeStatement(w.Body);
                break;

            case DoStatement d:
                AnalyzeStatement(d.Body);
                AnalyzeExpression(d.Condition);
                break;

            case ForStatement f:
                if (f.Initializer is not null) AnalyzeExpression(f.Initializer);
                if (f.Condition is not null) AnalyzeExpression(f.Condition);
                if (f.Iteration is not null) AnalyzeExpression(f.Iteration);
                AnalyzeStatement(f.Body);
                break;

            case SwitchStatement sw:
                AnalyzeExpression(sw.Expression);
                AnalyzeStatement(sw.Body);
                break;

            case CaseStatement c:
                AnalyzeExpression(c.Value);
                AnalyzeStatement(c.Statement);
                break;

            case DefaultStatement def:
                AnalyzeStatement(def.Statement);
                break;

            case LabeledStatement l:
                if (!_definedLabels.TryAdd(l.Label, l.Location))
                    Error(l.Location, $"redefinition of label '{l.Label}'");
                AnalyzeStatement(l.Statement);
                break;

            case GotoStatement g:
                _gotos.Add((g.Label, g.Location));
                break;

            case ReturnStatement r:
                if (r.Value is not null) AnalyzeExpression(r.Value);
                break;

            // break / continue carry no identifiers to resolve.
        }
    }

    private void EnterScope() => _current = new Scope(ScopeKind.Block, _current);
    private void ExitScope() => _current = _current.Parent ?? _global;

    // --- expressions (identifier resolution; full typing is a later slice) ------

    private void AnalyzeExpression(Expression expression, bool inCallPosition = false)
    {
        switch (expression)
        {
            case IdentifierExpression id:
                ResolveIdentifierUse(id, inCallPosition);
                break;

            case CallExpression call:
                AnalyzeExpression(call.Callee, inCallPosition: true);
                foreach (var arg in call.Arguments) AnalyzeExpression(arg);
                break;

            case IndexExpression idx:
                AnalyzeExpression(idx.Array);
                AnalyzeExpression(idx.Index);
                break;

            case MemberExpression m:
                AnalyzeExpression(m.Target);
                break;

            case UnaryExpression u:
                AnalyzeExpression(u.Operand);
                break;

            case PostfixExpression p:
                AnalyzeExpression(p.Operand);
                break;

            case SizeOfExpression s:
                AnalyzeExpression(s.Operand);
                break;

            case SizeOfTypeExpression st:
                ResolveTypeName(st.Type);
                break;

            case CastExpression cast:
                ResolveTypeName(cast.Type);
                AnalyzeExpression(cast.Operand);
                break;

            case BinaryExpression b:
                AnalyzeExpression(b.Left);
                AnalyzeExpression(b.Right);
                break;

            case ConditionalExpression c:
                AnalyzeExpression(c.Condition);
                AnalyzeExpression(c.WhenTrue);
                AnalyzeExpression(c.WhenFalse);
                break;

            case AssignmentExpression a:
                AnalyzeExpression(a.Target);
                AnalyzeExpression(a.Value);
                break;

            case CommaExpression comma:
                AnalyzeExpression(comma.Left);
                AnalyzeExpression(comma.Right);
                break;

            // literals resolve to nothing
        }
    }

    private void ResolveIdentifierUse(IdentifierExpression id, bool inCallPosition)
    {
        if (_current.LookupOrdinary(id.Name) is not null)
            return;

        if (inCallPosition)
        {
            // C89 §3.3.2.2: a call to an undeclared function implicitly declares int f().
            Warn(id.Location, $"implicit declaration of function '{id.Name}'");
            var implicitType = new QualType(new FunctionType(CTypes.Int, [], isVariadic: false, hasPrototype: false));
            _global.DeclareOrdinary(new Symbol(id.Name, SymbolKind.Function, implicitType, StorageClass.Extern, id.Location));
        }
        else
        {
            Error(id.Location, $"'{id.Name}' undeclared");
        }
    }

    private void AnalyzeInitializer(Initializer initializer)
    {
        switch (initializer)
        {
            case ExpressionInitializer e: AnalyzeExpression(e.Expression); break;
            case InitializerList l: foreach (var item in l.Items) AnalyzeInitializer(item); break;
        }
    }
}
