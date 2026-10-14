using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;
using AnsiCCore.Frontend.Parsing;
using AnsiCCore.Semantics.Analysis;
using AnsiCCore.Semantics.Symbols;
using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Tests.Semantics;

[TestFixture]
public class SemanticAnalyzerTests
{
    private static SemanticModel Analyze(string src)
    {
        var tokens = new Lexer(src, "t.c").Tokenize();
        var bag = new DiagnosticBag();
        var unit = new Parser(tokens, bag).ParseTranslationUnit();
        return SemanticAnalyzer.Analyze(unit, bag);
    }

    private static SemanticModel AnalyzeOk(string src)
    {
        var model = Analyze(src);
        Assert.That(model.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty,
            () => "unexpected errors: " + string.Join("; ", model.Diagnostics.Select(d => d.ToString())));
        return model;
    }

    private static Symbol Global(SemanticModel m, string name)
        => m.GlobalScope.LookupOrdinary(name) ?? throw new AssertionException($"symbol '{name}' not found");

    // --- basic declarations -----------------------------------------------

    [Test]
    public void Variable_ResolvesToBasicType()
        => Assert.That(Global(AnalyzeOk("int x;"), "x").Type.Type, Is.SameAs(CTypes.Int));

    [Test]
    public void Pointer_ResolvesToPointerType()
    {
        var p = Global(AnalyzeOk("char *p;"), "p").Type.Type;
        Assert.That(p, Is.InstanceOf<PointerType>());
        Assert.That(((PointerType)p).Pointee.Type, Is.SameAs(CTypes.Char));
    }

    [Test]
    public void Array_ResolvesLengthAndSize()
    {
        var a = (ArrayType)Global(AnalyzeOk("int a[10];"), "a").Type.Type;
        Assert.That(a.Length, Is.EqualTo(10));
        Assert.That(TypeOperations.SizeOf(a), Is.EqualTo(40));
    }

    [Test]
    public void Array_SizeFromEnumConstant()
    {
        var a = (ArrayType)Global(AnalyzeOk("enum { N = 4 }; int a[N];"), "a").Type.Type;
        Assert.That(a.Length, Is.EqualTo(4));
    }

    [Test]
    public void Array_SizeFromSizeofType()
    {
        var a = (ArrayType)Global(AnalyzeOk("int a[sizeof(int)];"), "a").Type.Type;
        Assert.That(a.Length, Is.EqualTo(4));
    }

    [Test]
    public void UnsignedLongInt_CombinesSpecifiers()
        => Assert.That(Global(AnalyzeOk("unsigned long int big;"), "big").Type.Type, Is.SameAs(CTypes.UnsignedLong));

    [Test]
    public void InvalidSpecifierCombination_Reports()
    {
        var model = Analyze("long float bad;");
        Assert.That(model.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.True);
    }

    // --- functions --------------------------------------------------------

    [Test]
    public void FunctionPrototype_ResolvesSignature()
    {
        var f = (FunctionType)Global(AnalyzeOk("int f(int, char *);"), "f").Type.Type;
        Assert.Multiple(() =>
        {
            Assert.That(f.ReturnType.Type, Is.SameAs(CTypes.Int));
            Assert.That(f.HasPrototype, Is.True);
            Assert.That(f.Parameters, Has.Count.EqualTo(2));
            Assert.That(f.Parameters[1].Type.Type, Is.InstanceOf<PointerType>());
        });
    }

    [Test]
    public void VoidParameterList_IsEmptyPrototype()
    {
        var f = (FunctionType)Global(AnalyzeOk("int f(void);"), "f").Type.Type;
        Assert.That(f.HasPrototype, Is.True);
        Assert.That(f.Parameters, Is.Empty);
    }

    [Test]
    public void EmptyParameterList_HasNoPrototype()
    {
        var f = (FunctionType)Global(AnalyzeOk("int f();"), "f").Type.Type;
        Assert.That(f.HasPrototype, Is.False);
    }

    [Test]
    public void ArrayParameter_DecaysToPointer()
    {
        var f = (FunctionType)Global(AnalyzeOk("int f(int a[]);"), "f").Type.Type;
        Assert.That(f.Parameters[0].Type.Type, Is.InstanceOf<PointerType>());
    }

    [Test]
    public void FunctionDefinition_ParametersAreInScopeInBody()
        => AnalyzeOk("int add(int a, int b) { return a + b; }");

    [Test]
    public void KandRFunction_ParameterTypesResolved()
        => AnalyzeOk("int add(a, b) int a; int b; { return a + b; }");

    // --- typedefs ---------------------------------------------------------

    [Test]
    public void Typedef_ResolvesToAliasedType()
    {
        var model = AnalyzeOk("typedef int myint; myint x;");
        Assert.That(Global(model, "x").Type.Type, Is.SameAs(CTypes.Int));
    }

    [Test]
    public void Typedef_PointerAlias()
    {
        var model = AnalyzeOk("typedef char *string; string s;");
        var s = Global(model, "s").Type.Type;
        Assert.That(s, Is.InstanceOf<PointerType>());
        Assert.That(((PointerType)s).Pointee.Type, Is.SameAs(CTypes.Char));
    }

    // --- struct / union / enum --------------------------------------------

    [Test]
    public void Struct_CompletesWithLayout()
    {
        var model = AnalyzeOk("struct P { int x; int y; };");
        var p = (RecordType)model.GlobalScope.LookupTag("P")!;
        Assert.Multiple(() =>
        {
            Assert.That(p.IsComplete, Is.True);
            Assert.That(p.Fields, Has.Count.EqualTo(2));
            Assert.That(p.Fields[0].Offset, Is.EqualTo(0));
            Assert.That(p.Fields[1].Offset, Is.EqualTo(4));
            Assert.That(p.Size, Is.EqualTo(8));
        });
    }

    [Test]
    public void Struct_MixedAlignment_PadsCorrectly()
    {
        var s = (RecordType)AnalyzeOk("struct S { char c; int i; };").GlobalScope.LookupTag("S")!;
        Assert.Multiple(() =>
        {
            Assert.That(s.Fields[0].Offset, Is.EqualTo(0)); // char c
            Assert.That(s.Fields[1].Offset, Is.EqualTo(4)); // int i (aligned to 4)
            Assert.That(s.Size, Is.EqualTo(8));
            Assert.That(s.Alignment, Is.EqualTo(4));
        });
    }

    [Test]
    public void Union_FieldsOverlap()
    {
        var u = (RecordType)AnalyzeOk("union U { int i; char c[8]; };").GlobalScope.LookupTag("U")!;
        Assert.Multiple(() =>
        {
            Assert.That(u.IsUnion, Is.True);
            Assert.That(u.Fields.All(f => f.Offset == 0), Is.True);
            Assert.That(u.Size, Is.EqualTo(8));
        });
    }

    [Test]
    public void Struct_SelfReferentialThroughPointer()
    {
        var node = (RecordType)AnalyzeOk("struct Node { int value; struct Node *next; };").GlobalScope.LookupTag("Node")!;
        var next = node.FindField("next")!;
        Assert.That(next.Type.Type, Is.InstanceOf<PointerType>());
        // The pointer's pointee is the very same record type (identity preserved).
        Assert.That(((PointerType)next.Type.Type).Pointee.Type, Is.SameAs(node));
    }

    [Test]
    public void Enum_AssignsSequentialAndExplicitValues()
    {
        var model = AnalyzeOk("enum Color { RED, GREEN = 5, BLUE };");
        Assert.Multiple(() =>
        {
            Assert.That(Global(model, "RED").EnumValue, Is.EqualTo(0));
            Assert.That(Global(model, "GREEN").EnumValue, Is.EqualTo(5));
            Assert.That(Global(model, "BLUE").EnumValue, Is.EqualTo(6));
            Assert.That(Global(model, "RED").Kind, Is.EqualTo(SymbolKind.EnumConstant));
        });
    }

    // --- scopes -----------------------------------------------------------

    [Test]
    public void UndeclaredIdentifier_IsReported()
    {
        var model = Analyze("int f(void) { return q; }");
        Assert.That(model.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("'q' undeclared")), Is.True);
    }

    [Test]
    public void BlockScopedLocal_NotVisibleAfterBlock()
    {
        var model = Analyze("int f(void) { { int local; } local = 1; return 0; }");
        Assert.That(model.Diagnostics.Any(d => d.Message.Contains("'local' undeclared")), Is.True);
    }

    [Test]
    public void ImplicitFunctionDeclaration_Warns()
    {
        var model = Analyze("int f(void) { return g(); }");
        Assert.Multiple(() =>
        {
            Assert.That(model.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("implicit declaration")), Is.True);
            Assert.That(model.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.False);
        });
    }

    [Test]
    public void IncompatibleRedeclaration_IsError()
    {
        var model = Analyze("int x; char x;");
        Assert.That(model.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("redeclaration")), Is.True);
    }

    [Test]
    public void CompatibleRedeclaration_IsAllowed()
        => AnalyzeOk("extern int x; int x;");

    [Test]
    public void UndefinedLabel_IsReported()
    {
        var model = Analyze("int f(void) { goto nowhere; return 0; }");
        Assert.That(model.Diagnostics.Any(d => d.Message.Contains("label 'nowhere'")), Is.True);
    }

    [Test]
    public void DefinedLabel_Resolves()
        => AnalyzeOk("int f(void) { goto done; done: return 0; }");

    [Test]
    public void TypedefShadowedByLocalVariable()
    {
        // Inside f, T is redeclared as an int object; using it as a value must not error.
        AnalyzeOk("typedef int T; int f(void) { int T; T = 5; return T; }");
    }
}
