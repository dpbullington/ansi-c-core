using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;
using AnsiCCore.Frontend.Parsing;

namespace AnsiCCore.Tests.Parsing;

[TestFixture]
public class ParserTests
{
    private static TranslationUnit Parse(string src, out DiagnosticBag diags)
    {
        var tokens = new Lexer(src, "test.c").Tokenize();
        var parser = new Parser(tokens);
        var unit = parser.ParseTranslationUnit();
        diags = parser.Diagnostics;
        return unit;
    }

    private static TranslationUnit ParseOk(string src)
    {
        var unit = Parse(src, out var diags);
        Assert.That(diags.Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty,
            () => "unexpected errors: " + string.Join("; ", diags.Select(d => d.ToString())));
        return unit;
    }

    /// <summary>Parses a single expression by wrapping it in a function body and drilling in.</summary>
    private static Expression ParseExpr(string expr)
    {
        var unit = ParseOk($"int _t(void) {{ {expr}; }}");
        var func = (FunctionDefinition)unit.Items[0];
        var stmt = (ExpressionStatement)func.Body.Items[0];
        return stmt.Expression!;
    }

    private static Declaration FirstDeclaration(string src)
        => (Declaration)ParseOk(src).Items[0];

    // --- expressions: precedence & associativity --------------------------

    [Test]
    public void Precedence_MultiplicationBindsTighterThanAddition()
    {
        var e = (BinaryExpression)ParseExpr("a + b * c");
        Assert.That(e.Operator, Is.EqualTo(TokenKind.Plus));
        Assert.That(((BinaryExpression)e.Right).Operator, Is.EqualTo(TokenKind.Star));
    }

    [Test]
    public void Associativity_SubtractionIsLeft()
    {
        var e = (BinaryExpression)ParseExpr("a - b - c");
        Assert.That(e.Operator, Is.EqualTo(TokenKind.Minus));
        Assert.That(e.Left, Is.InstanceOf<BinaryExpression>());   // (a - b) - c
        Assert.That(e.Right, Is.InstanceOf<IdentifierExpression>());
    }

    [Test]
    public void Associativity_AssignmentIsRight()
    {
        var e = (AssignmentExpression)ParseExpr("a = b = c");
        Assert.That(e.Value, Is.InstanceOf<AssignmentExpression>());
    }

    [Test]
    public void Ternary_Parses()
    {
        var e = (ConditionalExpression)ParseExpr("a ? b : c");
        Assert.That(e.Condition, Is.InstanceOf<IdentifierExpression>());
    }

    [Test]
    public void Comma_IsLowestPrecedence()
    {
        var e = (CommaExpression)ParseExpr("a = 1, b = 2");
        Assert.That(e.Left, Is.InstanceOf<AssignmentExpression>());
        Assert.That(e.Right, Is.InstanceOf<AssignmentExpression>());
    }

    [Test]
    public void Logical_BindsLooserThanComparison()
    {
        var e = (BinaryExpression)ParseExpr("a < b && c > d");
        Assert.That(e.Operator, Is.EqualTo(TokenKind.AmpAmp));
        Assert.That(((BinaryExpression)e.Left).Operator, Is.EqualTo(TokenKind.Less));
    }

    // --- expressions: unary / postfix / primary ---------------------------

    [Test]
    public void Postfix_CallIndexMemberChain()
    {
        var e = ParseExpr("obj->field[i].method(1, 2)");
        var call = (CallExpression)e;
        Assert.That(call.Arguments, Has.Count.EqualTo(2));
        var member = (MemberExpression)call.Callee;
        Assert.That(member.Arrow, Is.False);
        Assert.That(member.Target, Is.InstanceOf<IndexExpression>());
    }

    [Test]
    public void Unary_Dereference_And_AddressOf()
    {
        var deref = (UnaryExpression)ParseExpr("*p");
        Assert.That(deref.Operator, Is.EqualTo(TokenKind.Star));
        var addr = (UnaryExpression)ParseExpr("&x");
        Assert.That(addr.Operator, Is.EqualTo(TokenKind.Ampersand));
    }

    [Test]
    public void Prefix_And_Postfix_Increment_Differ()
    {
        Assert.That(ParseExpr("++x"), Is.InstanceOf<UnaryExpression>());
        Assert.That(ParseExpr("x++"), Is.InstanceOf<PostfixExpression>());
    }

    [Test]
    public void Sizeof_ExpressionVersusType()
    {
        Assert.That(ParseExpr("sizeof x"), Is.InstanceOf<SizeOfExpression>());
        Assert.That(ParseExpr("sizeof(int)"), Is.InstanceOf<SizeOfTypeExpression>());
    }

    [Test]
    public void Cast_Parses()
    {
        var cast = (CastExpression)ParseExpr("(char) x");
        Assert.That(cast.Operand, Is.InstanceOf<IdentifierExpression>());
    }

    [Test]
    public void AdjacentStringLiterals_Concatenate()
    {
        var s = (StringLiteral)ParseExpr("\"foo\" \"bar\"");
        Assert.That(s.Parts, Has.Count.EqualTo(2));
    }

    // --- declarators ------------------------------------------------------

    [Test]
    public void Declarator_ArrayOfPointers()
    {
        // int *a[10];  ->  a is array[10] of pointer to int  ->  Pointer(Array(Ident))
        var decl = FirstDeclaration("int *a[10];");
        var top = decl.InitDeclarators[0].Declarator;
        Assert.That(top, Is.InstanceOf<PointerDeclarator>());
        Assert.That(((PointerDeclarator)top).Inner, Is.InstanceOf<ArrayDeclarator>());
    }

    [Test]
    public void Declarator_PointerToFunction()
    {
        // int (*fp)(void);  ->  Function(Pointer(Ident fp))
        var decl = FirstDeclaration("int (*fp)(void);");
        var top = decl.InitDeclarators[0].Declarator;
        Assert.That(top, Is.InstanceOf<FunctionDeclarator>());
        var inner = ((FunctionDeclarator)top).Inner;
        Assert.That(inner, Is.InstanceOf<PointerDeclarator>());
        Assert.That(((PointerDeclarator)inner).Inner, Is.InstanceOf<IdentifierDeclarator>());
    }

    [Test]
    public void Declarator_MultiDimensionalArray()
    {
        var decl = FirstDeclaration("int m[3][4];");
        var top = (ArrayDeclarator)decl.InitDeclarators[0].Declarator;   // outer is [4]
        Assert.That(top.Inner, Is.InstanceOf<ArrayDeclarator>());         // inner is [3]
    }

    [Test]
    public void Declaration_MultipleDeclarators()
    {
        var decl = FirstDeclaration("int a, *b, c[5];");
        Assert.That(decl.InitDeclarators, Has.Count.EqualTo(3));
        Assert.That(decl.InitDeclarators[1].Declarator, Is.InstanceOf<PointerDeclarator>());
        Assert.That(decl.InitDeclarators[2].Declarator, Is.InstanceOf<ArrayDeclarator>());
    }

    [Test]
    public void Declaration_QualifiersAndStorageClass()
    {
        var decl = FirstDeclaration("static const int x = 0;");
        Assert.That(decl.Specifiers.StorageClasses, Does.Contain(TokenKind.KwStatic));
        Assert.That(decl.Specifiers.TypeQualifiers, Does.Contain(TokenKind.KwConst));
    }

    // --- struct / union / enum -------------------------------------------

    [Test]
    public void Struct_WithMembers()
    {
        var decl = FirstDeclaration("struct Point { int x; int y; };");
        var spec = (StructOrUnionSpecifier)decl.Specifiers.TypeSpecifiers[0];
        Assert.That(spec.IsUnion, Is.False);
        Assert.That(spec.Tag, Is.EqualTo("Point"));
        Assert.That(spec.Members, Has.Count.EqualTo(2));
    }

    [Test]
    public void Struct_IncompleteReference_HasNullMembers()
    {
        var decl = FirstDeclaration("struct Node *head;");
        var spec = (StructOrUnionSpecifier)decl.Specifiers.TypeSpecifiers[0];
        Assert.That(spec.Members, Is.Null);
    }

    [Test]
    public void Struct_Bitfields()
    {
        var decl = FirstDeclaration("struct Flags { unsigned a : 3; unsigned b : 1; };");
        var spec = (StructOrUnionSpecifier)decl.Specifiers.TypeSpecifiers[0];
        Assert.That(spec.Members![0].Declarators[0].BitWidth, Is.Not.Null);
    }

    [Test]
    public void Enum_WithExplicitValues()
    {
        var decl = FirstDeclaration("enum Color { RED, GREEN = 5, BLUE };");
        var spec = (EnumSpecifier)decl.Specifiers.TypeSpecifiers[0];
        Assert.That(spec.Enumerators, Has.Count.EqualTo(3));
        Assert.That(spec.Enumerators![1].Value, Is.Not.Null);
    }

    // --- initializers -----------------------------------------------------

    [Test]
    public void Initializer_Scalar()
    {
        var decl = FirstDeclaration("int x = 42;");
        Assert.That(decl.InitDeclarators[0].Initializer, Is.InstanceOf<ExpressionInitializer>());
    }

    [Test]
    public void Initializer_List_Nested()
    {
        var decl = FirstDeclaration("int m[2][2] = { {1, 2}, {3, 4} };");
        var list = (InitializerList)decl.InitDeclarators[0].Initializer!;
        Assert.That(list.Items, Has.Count.EqualTo(2));
        Assert.That(list.Items[0], Is.InstanceOf<InitializerList>());
    }

    [Test]
    public void Initializer_List_TrailingCommaAllowed()
        => Assert.DoesNotThrow(() => FirstDeclaration("int a[] = { 1, 2, 3, };"));

    // --- typedef disambiguation ------------------------------------------

    [Test]
    public void Typedef_NameBecomesAType()
    {
        var unit = ParseOk("typedef int myint; myint x;");
        Assert.That(unit.Items, Has.Count.EqualTo(2));
        var second = (Declaration)unit.Items[1];
        Assert.That(second.Specifiers.TypeSpecifiers[0], Is.InstanceOf<TypedefNameSpecifier>());
    }

    [Test]
    public void TypedefName_StarName_IsPointerDeclaration()
    {
        // With T a typedef, "T * p;" is a declaration of a pointer, not multiplication.
        var unit = ParseOk("typedef int T; void f(void) { T * p; }");
        var func = (FunctionDefinition)unit.Items[1];
        Assert.That(func.Body.Items[0], Is.InstanceOf<Declaration>());
    }

    [Test]
    public void NonTypedefName_StarName_IsMultiplicationStatement()
    {
        // Without a typedef, "a * b;" is an expression statement (multiply).
        var unit = ParseOk("void f(void) { a * b; }");
        var func = (FunctionDefinition)unit.Items[0];
        var stmt = (ExpressionStatement)func.Body.Items[0];
        Assert.That(((BinaryExpression)stmt.Expression!).Operator, Is.EqualTo(TokenKind.Star));
    }

    [Test]
    public void Cast_UsesTypedefName()
    {
        var unit = ParseOk("typedef int T; int g(void) { return (T) 0; }");
        var func = (FunctionDefinition)unit.Items[1];
        var ret = (ReturnStatement)func.Body.Items[0];
        Assert.That(ret.Value, Is.InstanceOf<CastExpression>());
    }

    // --- functions --------------------------------------------------------

    [Test]
    public void FunctionDefinition_Prototype()
    {
        var unit = ParseOk("int add(int a, int b) { return a + b; }");
        var func = (FunctionDefinition)unit.Items[0];
        var declarator = (FunctionDeclarator)func.Declarator;
        Assert.That(declarator.Parameters.Kind, Is.EqualTo(ParameterKind.Prototype));
        Assert.That(declarator.Parameters.Parameters, Has.Count.EqualTo(2));
    }

    [Test]
    public void FunctionDefinition_Variadic()
    {
        var unit = ParseOk("int printf(const char *fmt, ...) { return 0; }");
        var func = (FunctionDefinition)unit.Items[0];
        var declarator = (FunctionDeclarator)func.Declarator;
        Assert.That(declarator.Parameters.IsVariadic, Is.True);
    }

    [Test]
    public void FunctionDefinition_VoidParameterList_IsEmptyPrototype()
    {
        var unit = ParseOk("int main(void) { return 0; }");
        var func = (FunctionDefinition)unit.Items[0];
        var declarator = (FunctionDeclarator)func.Declarator;
        Assert.That(declarator.Parameters.Kind, Is.EqualTo(ParameterKind.Prototype));
        Assert.That(declarator.Parameters.Parameters, Is.Empty);
    }

    [Test]
    public void FunctionDefinition_KandR_OldStyle()
    {
        var unit = ParseOk("int add(a, b) int a; int b; { return a + b; }");
        var func = (FunctionDefinition)unit.Items[0];
        var declarator = (FunctionDeclarator)func.Declarator;
        Assert.That(declarator.Parameters.Kind, Is.EqualTo(ParameterKind.IdentifierList));
        Assert.That(declarator.Parameters.Identifiers, Has.Count.EqualTo(2));
        Assert.That(func.ParameterDeclarations, Has.Count.EqualTo(2));
    }

    [Test]
    public void FunctionPrototype_Declaration_NotDefinition()
    {
        var unit = ParseOk("int add(int, int);");
        Assert.That(unit.Items[0], Is.InstanceOf<Declaration>());
    }

    // --- statements -------------------------------------------------------

    [Test]
    public void IfElse_Parses()
    {
        var unit = ParseOk("void f(void) { if (a) b(); else c(); }");
        var func = (FunctionDefinition)unit.Items[0];
        var stmt = (IfStatement)func.Body.Items[0];
        Assert.That(stmt.Otherwise, Is.Not.Null);
    }

    [Test]
    public void DanglingElse_BindsToNearestIf()
    {
        var unit = ParseOk("void f(void) { if (a) if (b) x(); else y(); }");
        var func = (FunctionDefinition)unit.Items[0];
        var outer = (IfStatement)func.Body.Items[0];
        Assert.That(outer.Otherwise, Is.Null);                         // else binds to inner if
        Assert.That(((IfStatement)outer.Then).Otherwise, Is.Not.Null);
    }

    [Test]
    public void Loops_Parse()
    {
        var unit = ParseOk("""
            void f(void) {
                while (a) x();
                do y(); while (b);
                for (i = 0; i < n; i++) z();
            }
            """);
        var func = (FunctionDefinition)unit.Items[0];
        Assert.That(func.Body.Items[0], Is.InstanceOf<WhileStatement>());
        Assert.That(func.Body.Items[1], Is.InstanceOf<DoStatement>());
        Assert.That(func.Body.Items[2], Is.InstanceOf<ForStatement>());
    }

    [Test]
    public void For_WithEmptyClauses()
    {
        var unit = ParseOk("void f(void) { for (;;) break; }");
        var func = (FunctionDefinition)unit.Items[0];
        var loop = (ForStatement)func.Body.Items[0];
        Assert.Multiple(() =>
        {
            Assert.That(loop.Initializer, Is.Null);
            Assert.That(loop.Condition, Is.Null);
            Assert.That(loop.Iteration, Is.Null);
        });
    }

    [Test]
    public void Switch_WithCasesAndDefault()
    {
        var unit = ParseOk("""
            void f(void) {
                switch (x) {
                    case 1: a(); break;
                    case 2: b(); break;
                    default: c();
                }
            }
            """);
        var func = (FunctionDefinition)unit.Items[0];
        var sw = (SwitchStatement)func.Body.Items[0];
        var body = (CompoundStatement)sw.Body;
        Assert.That(body.Items[0], Is.InstanceOf<CaseStatement>());
    }

    [Test]
    public void LabelAndGoto_Parse()
    {
        var unit = ParseOk("void f(void) { goto done; done: return; }");
        var func = (FunctionDefinition)unit.Items[0];
        Assert.That(func.Body.Items[0], Is.InstanceOf<GotoStatement>());
        Assert.That(func.Body.Items[1], Is.InstanceOf<LabeledStatement>());
    }

    [Test]
    public void EmptyStatement_Parses()
    {
        var unit = ParseOk("void f(void) { ; }");
        var func = (FunctionDefinition)unit.Items[0];
        Assert.That(((ExpressionStatement)func.Body.Items[0]).Expression, Is.Null);
    }

    [Test]
    public void LocalDeclarationsThenStatements()
    {
        var unit = ParseOk("void f(void) { int x; int y = 2; x = y + 1; }");
        var func = (FunctionDefinition)unit.Items[0];
        Assert.That(func.Body.Items[0], Is.InstanceOf<Declaration>());
        Assert.That(func.Body.Items[2], Is.InstanceOf<ExpressionStatement>());
    }

    // --- error recovery ---------------------------------------------------

    [Test]
    public void SyntaxError_IsReported_AndParsingContinues()
    {
        // The first declaration is malformed; the second should still parse.
        var unit = Parse("int x = ;\nint y = 2;", out var diags);
        Assert.That(diags.HasErrors, Is.True);
        Assert.That(unit.Items.Any(i => i is Declaration d && d.InitDeclarators.Any(id => id.Declarator is IdentifierDeclarator { Name: "y" })), Is.True);
    }

    [Test]
    public void EmptyInput_ProducesEmptyUnit()
    {
        var unit = ParseOk("");
        Assert.That(unit.Items, Is.Empty);
    }
}
