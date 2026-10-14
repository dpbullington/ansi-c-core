using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;
using AnsiCCore.Frontend.Parsing;
using AnsiCCore.Semantics.Analysis;
using AnsiCCore.Semantics.Symbols;
using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Tests.Semantics;

[TestFixture]
public class ExpressionTypingTests
{
    /// <summary>
    /// Analyzes <c>globals</c> at file scope and <c>locals</c> + a trailing <c>expr;</c> inside a
    /// probe function, returning the model and the probed expression node.
    /// </summary>
    private static (SemanticModel Model, Expression Expr) Probe(string expr, string locals = "", string globals = "")
    {
        string src = $"{globals}\nint _probe(void) {{ {locals} {expr}; }}";
        var tokens = new Lexer(src, "t.c").Tokenize();
        var bag = new DiagnosticBag();
        var unit = new Parser(tokens, bag).ParseTranslationUnit();
        var model = SemanticAnalyzer.Analyze(unit, bag);

        var fn = unit.Items.OfType<FunctionDefinition>().Last();
        var stmt = fn.Body.Items.OfType<ExpressionStatement>().Last();
        return (model, stmt.Expression!);
    }

    private static ExpressionType TypeOk(string expr, string locals = "", string globals = "")
    {
        var (model, e) = Probe(expr, locals, globals);
        Assert.That(model.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty,
            () => "unexpected errors: " + string.Join("; ", model.Diagnostics.Select(d => d.ToString())));
        return model.TypeOf(e) ?? throw new AssertionException("expression was not typed");
    }

    private static CType TypeOfT(string expr, string locals = "", string globals = "")
        => TypeOk(expr, locals, globals).Type.Type;

    private static IReadOnlyList<Diagnostic> Errors(string expr, string locals = "", string globals = "")
        => Probe(expr, locals, globals).Model.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    // --- literals ---------------------------------------------------------

    [Test]
    public void IntegerLiteral_Types()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TypeOfT("1"), Is.SameAs(CTypes.Int));
            Assert.That(TypeOfT("1u"), Is.SameAs(CTypes.UnsignedInt));
            Assert.That(TypeOfT("1L"), Is.SameAs(CTypes.Long));
            Assert.That(TypeOfT("2147483648"), Is.SameAs(CTypes.Long));        // > INT_MAX
            Assert.That(TypeOfT("0xFFFFFFFF"), Is.SameAs(CTypes.UnsignedInt)); // hex fits unsigned int
        });
    }

    [Test]
    public void CharLiteral_IsInt() => Assert.That(TypeOfT("'a'"), Is.SameAs(CTypes.Int));

    [Test]
    public void FloatingLiteral_Types()
    {
        Assert.That(TypeOfT("1.0"), Is.SameAs(CTypes.Double));
        Assert.That(TypeOfT("1.0f"), Is.SameAs(CTypes.Float));
    }

    [Test]
    public void StringLiteral_IsCharArrayWithNulTerminator()
    {
        var t = TypeOk("\"abc\"");
        Assert.That(t.Type.Type, Is.InstanceOf<ArrayType>());
        Assert.That(((ArrayType)t.Type.Type).Length, Is.EqualTo(4));
        Assert.That(t.IsLvalue, Is.True);
    }

    // --- identifiers & lvalues -------------------------------------------

    [Test]
    public void Identifier_IsLvalueWithItsType()
    {
        var t = TypeOk("x", locals: "long x;");
        Assert.That(t.Type.Type, Is.SameAs(CTypes.Long));
        Assert.That(t.IsLvalue, Is.True);
    }

    // --- arithmetic & conversions ----------------------------------------

    [Test]
    public void Addition_AppliesUsualArithmeticConversions()
    {
        Assert.That(TypeOfT("a + b", "int a; int b;"), Is.SameAs(CTypes.Int));
        Assert.That(TypeOfT("a + b", "int a; long b;"), Is.SameAs(CTypes.Long));
        Assert.That(TypeOfT("a + b", "int a; double b;"), Is.SameAs(CTypes.Double));
    }

    [Test]
    public void SmallIntegerArithmetic_PromotesToInt()
        => Assert.That(TypeOfT("a + b", "char a; char b;"), Is.SameAs(CTypes.Int));

    [Test]
    public void Comparison_And_Logical_AreInt()
    {
        Assert.That(TypeOfT("a < b", "int a; int b;"), Is.SameAs(CTypes.Int));
        Assert.That(TypeOfT("a && b", "int a; int b;"), Is.SameAs(CTypes.Int));
    }

    // --- pointers ---------------------------------------------------------

    [Test]
    public void Dereference_YieldsPointeeLvalue()
    {
        var t = TypeOk("*p", "int *p;");
        Assert.That(t.Type.Type, Is.SameAs(CTypes.Int));
        Assert.That(t.IsLvalue, Is.True);
    }

    [Test]
    public void AddressOf_YieldsPointer()
    {
        var t = TypeOk("&a", "int a;");
        Assert.That(t.Type.Type, Is.InstanceOf<PointerType>());
        Assert.That(t.IsLvalue, Is.False);
    }

    [Test]
    public void Subscript_YieldsElementLvalue()
    {
        var t = TypeOk("a[2]", "int a[10];");
        Assert.That(t.Type.Type, Is.SameAs(CTypes.Int));
        Assert.That(t.IsLvalue, Is.True);
    }

    [Test]
    public void PointerPlusInteger_IsPointer()
        => Assert.That(TypeOfT("p + 1", "int *p;"), Is.InstanceOf<PointerType>());

    [Test]
    public void PointerMinusPointer_IsPtrdiff()
        => Assert.That(TypeOfT("p - q", "int *p; int *q;"), Is.SameAs(CTypes.Long));

    // --- sizeof / cast ----------------------------------------------------

    [Test]
    public void Sizeof_IsSizeType()
    {
        Assert.That(TypeOfT("sizeof(int)"), Is.SameAs(CTypes.UnsignedLong));
        Assert.That(TypeOfT("sizeof a", "int a;"), Is.SameAs(CTypes.UnsignedLong));
    }

    [Test]
    public void Cast_YieldsTargetType()
    {
        Assert.That(TypeOfT("(char) a", "int a;"), Is.SameAs(CTypes.Char));
        Assert.That(TypeOfT("(double) a", "int a;"), Is.SameAs(CTypes.Double));
    }

    // --- assignment / ternary / comma ------------------------------------

    [Test]
    public void Assignment_ResultIsTargetType()
        => Assert.That(TypeOfT("a = 3", "int a;"), Is.SameAs(CTypes.Int));

    [Test]
    public void Ternary_Arithmetic_UsesCommonType()
        => Assert.That(TypeOfT("a ? b : c", "int a; int b; double c;"), Is.SameAs(CTypes.Double));

    [Test]
    public void Ternary_PointerAndNull_IsPointer()
        => Assert.That(TypeOfT("a ? p : 0", "int a; int *p;"), Is.InstanceOf<PointerType>());

    [Test]
    public void Comma_ResultIsRightOperand()
        => Assert.That(TypeOfT("(a, c)", "int a; double c;"), Is.SameAs(CTypes.Double));

    // --- calls & members --------------------------------------------------

    [Test]
    public void Call_ResultIsReturnType()
        => Assert.That(TypeOfT("f(1, 2)", globals: "int f(int, int);"), Is.SameAs(CTypes.Int));

    [Test]
    public void Member_Dot_And_Arrow()
    {
        const string g = "struct S { int x; double y; };";
        var dot = TypeOk("s.x", "struct S s;", g);
        Assert.That(dot.Type.Type, Is.SameAs(CTypes.Int));
        Assert.That(dot.IsLvalue, Is.True);

        Assert.That(TypeOfT("ps->y", "struct S *ps;", g), Is.SameAs(CTypes.Double));
    }

    // --- error cases ------------------------------------------------------

    [Test]
    public void Dereference_NonPointer_IsError()
        => Assert.That(Errors("*a", "int a;").Any(d => d.Message.Contains("indirection")), Is.True);

    [Test]
    public void Call_NonFunction_IsError()
        => Assert.That(Errors("a()", "int a;").Any(d => d.Message.Contains("not a function")), Is.True);

    [Test]
    public void Member_OnNonStruct_IsError()
        => Assert.That(Errors("a.x", "int a;").Any(d => d.Message.Contains("not a structure")), Is.True);

    [Test]
    public void AddressOf_NonLvalue_IsError()
        => Assert.That(Errors("&(a + 1)", "int a;").Any(d => d.Message.Contains("lvalue required")), Is.True);

    [Test]
    public void AssignToConst_IsError()
        => Assert.That(Errors("a = 3", "const int a;").Any(d => d.Message.Contains("non-modifiable")), Is.True);

    [Test]
    public void ArithmeticOnPointer_IsError()
        => Assert.That(Errors("p * 2", "int *p;").Any(d => d.Message.Contains("arithmetic")), Is.True);

    [Test]
    public void CallArgumentCountMismatch_IsError()
        => Assert.That(Errors("f(1)", globals: "int f(int, int);").Any(d => d.Message.Contains("argument")), Is.True);

    [Test]
    public void CallArgumentTypeMismatch_IsError()
        => Assert.That(Errors("f(p)", "int *p;", "int f(int);").Any(d => d.Message.Contains("argument 1")), Is.True);

    [Test]
    public void NonScalarCondition_IsError()
        => Assert.That(Errors("s ? 1 : 2", "struct S { int x; } s;").Any(d => d.Message.Contains("scalar")), Is.True);

    [Test]
    public void ReturnTypeMismatch_IsError()
    {
        var tokens = new Lexer("int g(void) { int *p; return p; }", "t.c").Tokenize();
        var bag = new DiagnosticBag();
        var unit = new Parser(tokens, bag).ParseTranslationUnit();
        var model = SemanticAnalyzer.Analyze(unit, bag);
        Assert.That(model.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("return")), Is.True);
    }
}
