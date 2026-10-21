namespace AnsiCCore.Tests.Emit;

/// <summary>
/// Tests that several translation units compile as one program: external functions and globals
/// link across files, and identical type definitions (as from a shared header) are deduplicated.
/// </summary>
[TestFixture]
public class EmitMultiFileTests
{
    [Test]
    public void ExternalFunction_DefinedInAnotherFile()
    {
        const string lib = "int add(int a, int b) { return a + b; }";
        const string main = """
            extern int add(int a, int b);
            int main(void) { return add(19, 23); }
            """;
        Assert.That(EmitTestHost.RunMultiple(lib, main), Is.EqualTo(42));
    }

    [Test]
    public void ExternalGlobal_SharedAcrossFiles()
    {
        const string lib = """
            int counter = 100;
            void bump(void) { counter = counter + 1; }
            """;
        const string main = """
            extern int counter;
            extern void bump(void);
            int main(void) { bump(); bump(); return counter; }
            """;
        Assert.That(EmitTestHost.RunMultiple(lib, main), Is.EqualTo(102));
    }

    [Test]
    public void SharedStructDefinition_IsDeduplicated()
    {
        // The same struct appears in both files, as if from a shared header.
        const string pointC = """
            struct Point { int x; int y; };
            int dist2(struct Point *p) { return p->x * p->x + p->y * p->y; }
            """;
        const string mainC = """
            struct Point { int x; int y; };
            extern int dist2(struct Point *p);
            int main(void) { struct Point p; p.x = 3; p.y = 4; return dist2(&p); }
            """;
        Assert.That(EmitTestHost.RunMultiple(pointC, mainC), Is.EqualTo(25));
    }

    [Test]
    public void SharedEnumDefinition_IsDeduplicated()
    {
        const string a = """
            enum Color { RED, GREEN, BLUE };
            int value(enum Color c) { return c * 10; }
            """;
        const string b = """
            enum Color { RED, GREEN, BLUE };
            extern int value(enum Color c);
            int main(void) { return value(BLUE); } /* BLUE = 2 -> 20 */
            """;
        Assert.That(EmitTestHost.RunMultiple(a, b), Is.EqualTo(20));
    }

    [Test]
    public void ThreeFiles_MutualCalls()
    {
        const string a = "int square(int x) { return x * x; }";
        const string b = """
            extern int square(int x);
            int sum_of_squares(int n) { int s = 0; int i; for (i = 1; i <= n; i = i + 1) s = s + square(i); return s; }
            """;
        const string main = """
            extern int sum_of_squares(int n);
            int main(void) { return sum_of_squares(4); } /* 1+4+9+16 = 30 */
            """;
        Assert.That(EmitTestHost.RunMultiple(a, b, main), Is.EqualTo(30));
    }

    [Test]
    public void SameNamedStaticFunctions_StayIsolatedPerFile()
    {
        // Both files define a file-local `helper` with internal linkage. They must NOT collide:
        // each file's caller resolves to its own helper.
        const string a = """
            static int helper(void) { return 10; }
            int from_a(void) { return helper(); }
            """;
        const string b = """
            static int helper(void) { return 20; }
            int from_b(void) { return helper(); }
            """;
        const string main = """
            extern int from_a(void);
            extern int from_b(void);
            int main(void) { return from_a() + from_b(); } /* 10 + 20 = 30 */
            """;
        Assert.That(EmitTestHost.RunMultiple(a, b, main), Is.EqualTo(30));
    }

    [Test]
    public void SameNamedStaticGlobals_StayIsolatedPerFile()
    {
        // Each file has its own static `count`; bumping one must not disturb the other.
        const string a = """
            static int count = 1;
            int bump_a(void) { count = count + 5; return count; }
            """;
        const string b = """
            static int count = 100;
            int bump_b(void) { count = count + 7; return count; }
            """;
        const string main = """
            extern int bump_a(void);
            extern int bump_b(void);
            int main(void) { bump_a(); bump_b(); return bump_a() + bump_b(); } /* 11 + 114 = 125 */
            """;
        Assert.That(EmitTestHost.RunMultiple(a, b, main), Is.EqualTo(125));
    }

    [Test]
    public void StaticAndExternalSameName_CoexistAcrossFiles()
    {
        // File a has a file-local static `value`; file b exposes an external `value` the main uses.
        const string a = """
            static int value = 7;
            int local_value(void) { return value; }
            """;
        const string b = "int value = 99;";
        const string main = """
            extern int local_value(void);
            extern int value;
            int main(void) { return local_value() + value; } /* 7 + 99 = 106 */
            """;
        Assert.That(EmitTestHost.RunMultiple(a, b, main), Is.EqualTo(106));
    }

    [Test]
    public void IncompatibleStructRedefinition_IsAnError()
    {
        const string a = "struct S { int x; };\nint main(void) { return 0; }";
        const string b = "struct S { int x; int y; };"; // different body -> redefinition error
        var tokens1 = new AnsiCCore.Frontend.Lexing.Lexer(a, "a.c").Tokenize();
        var tokens2 = new AnsiCCore.Frontend.Lexing.Lexer(b, "b.c").Tokenize();
        var bag = new AnsiCCore.Frontend.Diagnostics.DiagnosticBag();
        var items = new List<AnsiCCore.Frontend.Ast.Node>();
        items.AddRange(new AnsiCCore.Frontend.Parsing.Parser(tokens1, bag).ParseTranslationUnit().Items);
        items.AddRange(new AnsiCCore.Frontend.Parsing.Parser(tokens2, bag).ParseTranslationUnit().Items);
        var program = new AnsiCCore.Frontend.Ast.TranslationUnit(default, items);
        AnsiCCore.Semantics.Analysis.SemanticAnalyzer.Analyze(program, bag);
        Assert.That(bag.Any(d => d.Message.Contains("redefinition")), Is.True);
    }
}
