using System.Text;
using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Lexing;

namespace AnsiCCore.Driver;

/// <summary>
/// Renders a parse tree as an indented outline for inspection (the CLI <c>--dump-ast</c>
/// option). Expressions are rendered compactly in a C-like form; declarations, statements,
/// and function definitions become a nested tree. This is a debugging aid, not a C
/// pretty-printer — it favors showing structure over reproducing exact source.
/// </summary>
public static class AstPrinter
{
    public static string Print(TranslationUnit unit)
    {
        var sb = new StringBuilder();
        foreach (var item in unit.Items)
            PrintNode(sb, item, 0);
        return sb.ToString();
    }

    private static void Line(StringBuilder sb, int indent, string text)
        => sb.Append(' ', indent * 2).Append(text).Append('\n');

    private static void PrintNode(StringBuilder sb, Node node, int indent)
    {
        switch (node)
        {
            case FunctionDefinition f:
                Line(sb, indent, $"function {RenderDeclarator(f.Declarator)} : {RenderSpecifiers(f.Specifiers)}");
                PrintStatement(sb, f.Body, indent + 1);
                break;
            case Declaration d:
                PrintDeclaration(sb, d, indent);
                break;
            case Statement s:
                PrintStatement(sb, s, indent);
                break;
            default:
                Line(sb, indent, node.GetType().Name);
                break;
        }
    }

    private static void PrintDeclaration(StringBuilder sb, Declaration d, int indent)
    {
        string specs = RenderSpecifiers(d.Specifiers);
        if (d.InitDeclarators.Count == 0)
        {
            Line(sb, indent, $"declaration: {specs}");
            return;
        }
        foreach (var init in d.InitDeclarators)
        {
            string text = $"declare {RenderDeclarator(init.Declarator)} : {specs}";
            if (init.Initializer != null)
                text += $" = {RenderInitializer(init.Initializer)}";
            Line(sb, indent, text);
        }
    }

    private static void PrintStatement(StringBuilder sb, Statement s, int indent)
    {
        switch (s)
        {
            case CompoundStatement c:
                Line(sb, indent, "{");
                foreach (var item in c.Items) PrintNode(sb, item, indent + 1);
                Line(sb, indent, "}");
                break;
            case ExpressionStatement e:
                Line(sb, indent, e.Expression is null ? ";" : RenderExpr(e.Expression) + ";");
                break;
            case IfStatement i:
                Line(sb, indent, $"if ({RenderExpr(i.Condition)})");
                PrintStatement(sb, i.Then, indent + 1);
                if (i.Otherwise != null) { Line(sb, indent, "else"); PrintStatement(sb, i.Otherwise, indent + 1); }
                break;
            case WhileStatement w:
                Line(sb, indent, $"while ({RenderExpr(w.Condition)})");
                PrintStatement(sb, w.Body, indent + 1);
                break;
            case DoStatement d:
                Line(sb, indent, "do");
                PrintStatement(sb, d.Body, indent + 1);
                Line(sb, indent, $"while ({RenderExpr(d.Condition)})");
                break;
            case ForStatement f:
                Line(sb, indent, $"for ({Opt(f.Initializer)}; {Opt(f.Condition)}; {Opt(f.Iteration)})");
                PrintStatement(sb, f.Body, indent + 1);
                break;
            case SwitchStatement sw:
                Line(sb, indent, $"switch ({RenderExpr(sw.Expression)})");
                PrintStatement(sb, sw.Body, indent + 1);
                break;
            case CaseStatement c:
                Line(sb, indent, $"case {RenderExpr(c.Value)}:");
                PrintStatement(sb, c.Statement, indent + 1);
                break;
            case DefaultStatement def:
                Line(sb, indent, "default:");
                PrintStatement(sb, def.Statement, indent + 1);
                break;
            case LabeledStatement l:
                Line(sb, indent, $"{l.Label}:");
                PrintStatement(sb, l.Statement, indent + 1);
                break;
            case ReturnStatement r:
                Line(sb, indent, r.Value is null ? "return;" : $"return {RenderExpr(r.Value)};");
                break;
            case GotoStatement g:
                Line(sb, indent, $"goto {g.Label};");
                break;
            case BreakStatement:
                Line(sb, indent, "break;");
                break;
            case ContinueStatement:
                Line(sb, indent, "continue;");
                break;
            default:
                Line(sb, indent, s.GetType().Name);
                break;
        }
    }

    private static string Opt(Expression? e) => e is null ? string.Empty : RenderExpr(e);

    // --- compact renderers ------------------------------------------------

    private static string RenderSpecifiers(DeclarationSpecifiers specs)
    {
        var parts = new List<string>();
        parts.AddRange(specs.StorageClasses.Select(TokenSpelling.Spell));
        parts.AddRange(specs.TypeQualifiers.Select(TokenSpelling.Spell));
        parts.AddRange(specs.TypeSpecifiers.Select(RenderTypeSpecifier));
        return parts.Count == 0 ? "(implicit int)" : string.Join(' ', parts);
    }

    private static string RenderTypeSpecifier(TypeSpecifier ts) => ts switch
    {
        PrimitiveTypeSpecifier p => TokenSpelling.Spell(p.Keyword),
        TypedefNameSpecifier t => t.Name,
        StructOrUnionSpecifier s => $"{(s.IsUnion ? "union" : "struct")} {s.Tag ?? "<anonymous>"}"
                                    + (s.Members is null ? string.Empty : $" {{{s.Members.Count} member(s)}}"),
        EnumSpecifier e => $"enum {e.Tag ?? "<anonymous>"}"
                           + (e.Enumerators is null ? string.Empty : $" {{{e.Enumerators.Count} value(s)}}"),
        _ => ts.GetType().Name,
    };

    /// <summary>Renders a declarator's shape inside-out, e.g. <c>fp : pointer to function(void)</c>.</summary>
    private static string RenderDeclarator(Declarator d)
    {
        string name = NameOf(d) ?? "<abstract>";
        string shape = RenderDeclaratorShape(d);
        return shape.Length == 0 ? name : $"{name} ({shape})";
    }

    private static string RenderDeclaratorShape(Declarator d) => d switch
    {
        IdentifierDeclarator => string.Empty,
        PointerDeclarator p => Join("pointer to", RenderDeclaratorShape(p.Inner)),
        ArrayDeclarator a => Join($"array{(a.Size is null ? string.Empty : $"[{RenderExpr(a.Size)}]")} of", RenderDeclaratorShape(a.Inner)),
        FunctionDeclarator f => Join($"function({RenderParameters(f.Parameters)}) returning", RenderDeclaratorShape(f.Inner)),
        _ => string.Empty,
    };

    private static string Join(string head, string tail) => tail.Length == 0 ? head : $"{head} {tail}";

    private static string RenderParameters(FunctionParameters p) => p.Kind switch
    {
        ParameterKind.Empty => "unspecified",
        ParameterKind.IdentifierList => string.Join(", ", p.Identifiers),
        _ => (p.Parameters.Count == 0 ? "void" : string.Join(", ", p.Parameters.Select(RenderParameter)))
             + (p.IsVariadic ? ", ..." : string.Empty),
    };

    private static string RenderParameter(ParameterDeclaration p)
    {
        string specs = RenderSpecifiers(p.Specifiers);
        return p.Declarator is null ? specs : $"{specs} {RenderDeclarator(p.Declarator)}";
    }

    private static string? NameOf(Declarator d) => d switch
    {
        IdentifierDeclarator id => id.Name,
        PointerDeclarator p => NameOf(p.Inner),
        ArrayDeclarator a => NameOf(a.Inner),
        FunctionDeclarator f => NameOf(f.Inner),
        _ => null,
    };

    private static string RenderInitializer(Initializer init) => init switch
    {
        ExpressionInitializer e => RenderExpr(e.Expression),
        InitializerList l => "{ " + string.Join(", ", l.Items.Select(RenderInitializer)) + " }",
        _ => init.GetType().Name,
    };

    private static string RenderExpr(Expression e) => e switch
    {
        IdentifierExpression id => id.Name,
        IntegerLiteral i => i.Text,
        FloatingLiteral f => f.Text,
        CharacterLiteral c => c.Text,
        StringLiteral s => string.Join(" ", s.Parts),
        CallExpression call => $"{RenderExpr(call.Callee)}({string.Join(", ", call.Arguments.Select(RenderExpr))})",
        IndexExpression idx => $"{RenderExpr(idx.Array)}[{RenderExpr(idx.Index)}]",
        MemberExpression m => $"{RenderExpr(m.Target)}{(m.Arrow ? "->" : ".")}{m.Member}",
        UnaryExpression u => $"{TokenSpelling.Spell(u.Operator)}{RenderExpr(u.Operand)}",
        PostfixExpression p => $"{RenderExpr(p.Operand)}{TokenSpelling.Spell(p.Operator)}",
        SizeOfExpression so => $"sizeof {RenderExpr(so.Operand)}",
        SizeOfTypeExpression st => $"sizeof({RenderTypeName(st.Type)})",
        CastExpression cast => $"({RenderTypeName(cast.Type)}){RenderExpr(cast.Operand)}",
        BinaryExpression b => $"({RenderExpr(b.Left)} {TokenSpelling.Spell(b.Operator)} {RenderExpr(b.Right)})",
        ConditionalExpression c => $"({RenderExpr(c.Condition)} ? {RenderExpr(c.WhenTrue)} : {RenderExpr(c.WhenFalse)})",
        AssignmentExpression a => $"({RenderExpr(a.Target)} {TokenSpelling.Spell(a.Operator)} {RenderExpr(a.Value)})",
        CommaExpression comma => $"({RenderExpr(comma.Left)}, {RenderExpr(comma.Right)})",
        _ => e.GetType().Name,
    };

    private static string RenderTypeName(TypeName t)
    {
        string specs = RenderSpecifiers(t.Specifiers);
        return t.Declarator is null ? specs : $"{specs} {RenderDeclarator(t.Declarator)}";
    }
}
