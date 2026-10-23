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

        // Old-style (K&R) definition: the declarator carried only parameter names; resolve their
        // types from the definition's declaration block and rebuild the function type so it (and
        // therefore Emit) sees the parameters.
        var funcDeclarator = FindFunctionDeclarator(f.Declarator);
        if (functionType is { HasPrototype: false }
            && funcDeclarator is { Parameters: { Kind: ParameterKind.IdentifierList, Identifiers.Count: > 0 } })
        {
            functionType = new FunctionType(functionType.ReturnType, ResolveKnrParameters(f, funcDeclarator),
                isVariadic: false, hasPrototype: false);
            type = new QualType(functionType);
        }

        var symbol = new Symbol(name, SymbolKind.Function, type, storage, f.Location) { IsDefined = true };
        DeclareOrdinaryChecked(symbol, fileScope: true);
        _symbols[f] = symbol;

        // Set up the per-function context.
        _definedLabels.Clear();
        _gotos.Clear();
        _currentFunctionReturn = functionType?.ReturnType ?? baseType;

        // Parameters share the function's top-level block scope, which nests under the file scope
        // so the function can see its file's static functions/globals plus the shared externals.
        var previous = _current;
        _current = new Scope(ScopeKind.Block, _currentFileScope);
        _functionParameters[f] = DeclareParameters(f, functionType);

        foreach (var item in f.Body.Items)
            AnalyzeBlockItem(item);

        _current = previous;

        foreach (var (label, loc) in _gotos)
            if (!_definedLabels.ContainsKey(label))
                Error(loc, $"label '{label}' used but not defined");

        _currentFunctionReturn = null;
    }

    /// <summary>Resolves an old-style (K&amp;R) parameter list: names from the identifier list, types
    /// from the definition's declaration block (a name with no declaration defaults to <c>int</c>).</summary>
    private List<Parameter> ResolveKnrParameters(FunctionDefinition f, FunctionDeclarator funcDeclarator)
    {
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

        return funcDeclarator.Parameters.Identifiers
            .Select(n => new Parameter(types.GetValueOrDefault(n, new QualType(CTypes.Int)), n))
            .ToList();
    }

    /// <summary>Declares a function's parameters in its scope (both prototype and K&amp;R, since the
    /// K&amp;R type was rebuilt with its parameters) and returns them in order for Emit.</summary>
    private List<Symbol> DeclareParameters(FunctionDefinition f, FunctionType? functionType)
    {
        var result = new List<Symbol>();
        if (functionType is null) return result;

        // Prototype parameter AST nodes (if any) let us key each symbol to its node; K&R has none.
        var nodes = FindFunctionDeclarator(f.Declarator)?.Parameters.Parameters;
        for (int i = 0; i < functionType.Parameters.Count; i++)
        {
            var p = functionType.Parameters[i];
            if (p.Name is null) continue;
            var sym = new Symbol(p.Name, SymbolKind.Parameter, p.Type, StorageClass.None, f.Location);
            _current.DeclareOrdinary(sym);
            result.Add(sym);
            if (nodes is not null && i < nodes.Count)
                _symbols[nodes[i]] = sym;
        }
        return result;
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
                if (e.Expression is not null) TypeExpression(e.Expression);
                break;

            case IfStatement i:
                RequireScalarControl(i.Condition, "if");
                AnalyzeStatement(i.Then);
                if (i.Otherwise is not null) AnalyzeStatement(i.Otherwise);
                break;

            case WhileStatement w:
                RequireScalarControl(w.Condition, "while");
                AnalyzeStatement(w.Body);
                break;

            case DoStatement d:
                AnalyzeStatement(d.Body);
                RequireScalarControl(d.Condition, "do-while");
                break;

            case ForStatement f:
                if (f.Initializer is not null) TypeExpression(f.Initializer);
                if (f.Condition is not null) RequireScalarControl(f.Condition, "for");
                if (f.Iteration is not null) TypeExpression(f.Iteration);
                AnalyzeStatement(f.Body);
                break;

            case SwitchStatement sw:
                TypeExpression(sw.Expression);
                AnalyzeStatement(sw.Body);
                break;

            case CaseStatement c:
                TypeExpression(c.Value);
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
                AnalyzeReturn(r);
                break;

            // break / continue carry no identifiers to resolve.
        }
    }

    private void EnterScope() => _current = new Scope(ScopeKind.Block, _current);
    private void ExitScope() => _current = _current.Parent ?? _global;

    private void RequireScalarControl(Expression condition, string context)
    {
        var info = TypeExpression(condition);
        var t = TypeOperations.Decay(info.Type).Type;
        if (!TypeOperations.IsError(t) && !TypeOperations.IsScalar(t))
            Error(condition.Location, $"controlling expression of '{context}' must have scalar type, not '{info.Type}'");
    }

    private void AnalyzeReturn(ReturnStatement r)
    {
        var expected = _currentFunctionReturn ?? new QualType(CTypes.Int);
        bool returnsVoid = TypeOperations.IsVoid(expected.Type);

        if (r.Value is null)
        {
            if (!returnsVoid)
                Warn(r.Location, "'return' with no value in a function returning non-void");
            return;
        }

        var value = Decay(TypeExpression(r.Value));
        if (returnsVoid)
            Warn(r.Location, "'return' with a value in a function returning void");
        else
            CheckAssignable(expected, value, r.Location, "return");
    }

    private void AnalyzeInitializer(Initializer initializer)
    {
        switch (initializer)
        {
            case ExpressionInitializer e: TypeExpression(e.Expression); break;
            case InitializerList l: foreach (var item in l.Items) AnalyzeInitializer(item); break;
        }
    }
}
