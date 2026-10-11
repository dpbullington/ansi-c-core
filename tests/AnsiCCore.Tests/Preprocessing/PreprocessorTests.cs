using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;
using AnsiCCore.Frontend.Preprocessing;

namespace AnsiCCore.Tests.Preprocessing;

[TestFixture]
public class PreprocessorTests
{
    private static readonly PreprocessorOptions FixedClock = new()
    {
        Clock = new DateTime(2026, 10, 11, 13, 45, 7),
    };

    private static IReadOnlyList<Token> Pp(string src, out DiagnosticBag diags,
        IDictionary<string, string>? files = null, PreprocessorOptions? options = null)
    {
        var resolver = new InMemoryIncludeResolver(files ?? new Dictionary<string, string>());
        var pp = new Preprocessor(resolver, options: options ?? FixedClock);
        var tokens = pp.Preprocess(src, "main.c");
        diags = pp.Diagnostics;
        return tokens;
    }

    /// <summary>Renders the preprocessed stream as space-joined lexemes (sans EOF) for easy asserts.</summary>
    private static string Render(string src, IDictionary<string, string>? files = null, PreprocessorOptions? options = null)
    {
        var tokens = Pp(src, out _, files, options);
        return string.Join(" ", tokens.Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Text));
    }

    private static string RenderOk(string src, IDictionary<string, string>? files = null, PreprocessorOptions? options = null)
    {
        var tokens = Pp(src, out var diags, files, options);
        Assert.That(diags.Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty,
            () => "unexpected errors: " + string.Join("; ", diags.Select(d => d.ToString())));
        return string.Join(" ", tokens.Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Text));
    }

    // --- passthrough ------------------------------------------------------

    [Test]
    public void PlainCode_PassesThroughUnchanged()
        => Assert.That(RenderOk("int x = 1 ;"), Is.EqualTo("int x = 1 ;"));

    // --- object-like macros ----------------------------------------------

    [Test]
    public void ObjectMacro_IsExpanded()
        => Assert.That(RenderOk("#define N 10\nint a[N];"), Is.EqualTo("int a [ 10 ] ;"));

    [Test]
    public void ObjectMacro_ExpandsRecursivelyThroughOtherMacros()
        => Assert.That(RenderOk("#define A B\n#define B 42\nA"), Is.EqualTo("42"));

    [Test]
    public void Undef_StopsExpansion()
        => Assert.That(RenderOk("#define N 10\n#undef N\nN"), Is.EqualTo("N"));

    [Test]
    public void SelfReferentialMacro_IsNotInfinite()
        => Assert.That(RenderOk("#define A A\nA"), Is.EqualTo("A"));

    [Test]
    public void MutuallyRecursiveMacros_Terminate()
        => Assert.That(RenderOk("#define A B\n#define B A\nA"), Is.EqualTo("A"));

    // --- function-like macros --------------------------------------------

    [Test]
    public void FunctionMacro_SubstitutesArguments()
        => Assert.That(RenderOk("#define ADD(a,b) ((a)+(b))\nADD(1,2)"), Is.EqualTo("( ( 1 ) + ( 2 ) )"));

    [Test]
    public void FunctionMacro_ArgumentsAreMacroExpanded()
        => Assert.That(RenderOk("#define ONE 1\n#define ID(x) x\nID(ONE)"), Is.EqualTo("1"));

    [Test]
    public void FunctionMacro_NameWithoutParensIsNotExpanded()
        => Assert.That(RenderOk("#define F(x) x\nF + 1"), Is.EqualTo("F + 1"));

    [Test]
    public void FunctionMacro_SpaceBeforeParenStillInvokes()
        => Assert.That(RenderOk("#define F(x) x\nF (7)"), Is.EqualTo("7"));

    [Test]
    public void ObjectMacro_WithSpaceBeforeParen_IsObjectLike()
        => Assert.That(RenderOk("#define F (x)\nF"), Is.EqualTo("( x )"));

    [Test]
    public void FunctionMacro_ArgumentsCanSpanLines()
        => Assert.That(RenderOk("#define ADD(a,b) a+b\nADD(1,\n2)"), Is.EqualTo("1 + 2"));

    [Test]
    public void FunctionMacro_NestedParensInArgument()
        => Assert.That(RenderOk("#define ID(x) x\nID((1,2))"), Is.EqualTo("( 1 , 2 )"));

    [Test]
    public void FunctionMacro_EmptyArgument()
        => Assert.That(RenderOk("#define ID(x) [x]\nID()"), Is.EqualTo("[ ]"));

    [Test]
    public void FunctionMacro_WrongArgCount_ReportsError()
    {
        Pp("#define ADD(a,b) a+b\nADD(1)", out var diags);
        Assert.That(diags.HasErrors, Is.True);
    }

    // --- stringize and paste ---------------------------------------------

    [Test]
    public void Stringize_ProducesStringLiteral()
        => Assert.That(RenderOk("#define STR(x) #x\nSTR(hello world)"), Is.EqualTo("\"hello world\""));

    [Test]
    public void Stringize_EscapesQuotesAndBackslashes()
        => Assert.That(RenderOk("#define STR(x) #x\nSTR(\"a\\b\")"), Is.EqualTo("\"\\\"a\\\\b\\\"\""));

    [Test]
    public void TokenPaste_JoinsTokens()
        => Assert.That(RenderOk("#define CAT(a,b) a##b\nCAT(foo,bar)"), Is.EqualTo("foobar"));

    [Test]
    public void TokenPaste_BuildsNumber()
        => Assert.That(RenderOk("#define CAT(a,b) a##b\nCAT(12,34)"), Is.EqualTo("1234"));

    [Test]
    public void TokenPaste_WithEmptyOperand()
        => Assert.That(RenderOk("#define CAT(a,b) a##b\nCAT(foo,)"), Is.EqualTo("foo"));

    [Test]
    public void Stringize_AppliedToExpandedViaIndirection()
    {
        // The classic xstr trick: argument is expanded before stringizing.
        const string src = "#define str(x) #x\n#define xstr(x) str(x)\n#define N 10\nxstr(N)";
        Assert.That(RenderOk(src), Is.EqualTo("\"10\""));
    }

    // --- conditionals -----------------------------------------------------

    [Test]
    public void IfdefIncludesWhenDefined()
        => Assert.That(RenderOk("#define FEATURE\n#ifdef FEATURE\non\n#else\noff\n#endif"), Is.EqualTo("on"));

    [Test]
    public void IfndefIncludesWhenNotDefined()
        => Assert.That(RenderOk("#ifndef FEATURE\noff\n#else\non\n#endif"), Is.EqualTo("off"));

    [Test]
    public void If_EvaluatesArithmetic()
        => Assert.That(RenderOk("#if 2 + 3 * 4 == 14\nyes\n#endif"), Is.EqualTo("yes"));

    [Test]
    public void If_ElifElseChain_PicksFirstTrueBranch()
    {
        const string src = "#if 0\na\n#elif 1\nb\n#elif 1\nc\n#else\nd\n#endif";
        Assert.That(RenderOk(src), Is.EqualTo("b"));
    }

    [Test]
    public void If_DefinedOperator_BothForms()
    {
        Assert.That(RenderOk("#define X\n#if defined X && defined(X)\nok\n#endif"), Is.EqualTo("ok"));
        Assert.That(RenderOk("#if !defined(Y)\nno_y\n#endif"), Is.EqualTo("no_y"));
    }

    [Test]
    public void If_UndefinedIdentifierIsZero()
        => Assert.That(RenderOk("#if UNDEFINED_THING\nbad\n#else\ngood\n#endif"), Is.EqualTo("good"));

    [Test]
    public void If_MacroExpandedInCondition()
        => Assert.That(RenderOk("#define LEVEL 3\n#if LEVEL >= 2\nhigh\n#endif"), Is.EqualTo("high"));

    [Test]
    public void NestedConditionals_InactiveOuterSkipsInner()
    {
        const string src = "#if 0\n#if 1\na\n#endif\nb\n#endif\nc";
        Assert.That(RenderOk(src), Is.EqualTo("c"));
    }

    [Test]
    public void If_ShortCircuitAvoidsDivideByZero()
        => Assert.That(RenderOk("#if 1 || (1/0)\nok\n#endif"), Is.EqualTo("ok"));

    [Test]
    public void If_CharacterConstant()
        => Assert.That(RenderOk("#if 'A' == 65\nascii\n#endif"), Is.EqualTo("ascii"));

    [Test]
    public void UnterminatedConditional_ReportsError()
    {
        Pp("#if 1\nstuff", out var diags);
        Assert.That(diags.HasErrors, Is.True);
    }

    [Test]
    public void EndifWithoutIf_ReportsError()
    {
        Pp("#endif", out var diags);
        Assert.That(diags.HasErrors, Is.True);
    }

    // --- #include ---------------------------------------------------------

    [Test]
    public void Include_Quoted_SplicesContent()
    {
        var files = new Dictionary<string, string> { ["helper.h"] = "int helper ;" };
        Assert.That(RenderOk("#include \"helper.h\"\nint main ;", files), Is.EqualTo("int helper ; int main ;"));
    }

    [Test]
    public void Include_Angled_ReconstructsPath()
    {
        var files = new Dictionary<string, string> { ["sys/types.h"] = "typedef int myint ;" };
        Assert.That(RenderOk("#include <sys/types.h>", files), Is.EqualTo("typedef int myint ;"));
    }

    [Test]
    public void Include_MacrosFromHeaderAreVisibleAfterward()
    {
        var files = new Dictionary<string, string> { ["cfg.h"] = "#define SIZE 256" };
        Assert.That(RenderOk("#include \"cfg.h\"\nchar buf[SIZE];", files), Is.EqualTo("char buf [ 256 ] ;"));
    }

    [Test]
    public void Include_IncludeGuard_PreventsDoubleDefinition()
    {
        var files = new Dictionary<string, string>
        {
            ["guard.h"] = "#ifndef GUARD_H\n#define GUARD_H\nint once ;\n#endif",
        };
        Assert.That(RenderOk("#include \"guard.h\"\n#include \"guard.h\"", files), Is.EqualTo("int once ;"));
    }

    [Test]
    public void Include_MissingFile_ReportsError()
    {
        Pp("#include \"nope.h\"", out var diags);
        Assert.That(diags.HasErrors, Is.True);
    }

    // --- predefined / dynamic macros -------------------------------------

    [Test]
    public void Stdc_IsDefined()
        => Assert.That(RenderOk("#if __STDC__\nstandard\n#endif"), Is.EqualTo("standard"));

    [Test]
    public void Line_ReflectsSourceLine()
    {
        // __LINE__ is on the 3rd line.
        Assert.That(RenderOk("\n\n__LINE__"), Is.EqualTo("3"));
    }

    [Test]
    public void File_ReflectsFileName()
        => Assert.That(RenderOk("__FILE__"), Is.EqualTo("\"main.c\""));

    [Test]
    public void DateAndTime_UseInjectedClock()
    {
        Assert.That(RenderOk("__DATE__"), Is.EqualTo("\"Oct 11 2026\""));
        Assert.That(RenderOk("__TIME__"), Is.EqualTo("\"13:45:07\""));
    }

    [Test]
    public void PredefinedMacroFromOptions_IsAvailable()
    {
        var options = new PreprocessorOptions
        {
            Clock = FixedClock.Clock,
            PredefinedMacros = new Dictionary<string, string?> { ["DEBUG"] = "1", ["VERSION"] = "\"2.0\"" },
        };
        Assert.That(RenderOk("#if DEBUG\ndbg\n#endif", options: options), Is.EqualTo("dbg"));
        Assert.That(RenderOk("VERSION", options: options), Is.EqualTo("\"2.0\""));
    }

    // --- error directive --------------------------------------------------

    [Test]
    public void ErrorDirective_InActiveBranch_Reports()
    {
        Pp("#error something bad", out var diags);
        Assert.That(diags.Single(d => d.Severity == DiagnosticSeverity.Error).Message, Does.Contain("something bad"));
    }

    [Test]
    public void ErrorDirective_InInactiveBranch_IsSilent()
    {
        Pp("#if 0\n#error not reached\n#endif", out var diags);
        Assert.That(diags.HasErrors, Is.False);
    }

    // --- redefinition -----------------------------------------------------

    [Test]
    public void IdenticalRedefinition_IsSilent()
    {
        Pp("#define N 1\n#define N 1\nN", out var diags);
        Assert.That(diags, Is.Empty);
    }

    [Test]
    public void IncompatibleRedefinition_Warns()
    {
        Pp("#define N 1\n#define N 2\nN", out var diags);
        Assert.That(diags.Any(d => d.Severity == DiagnosticSeverity.Warning), Is.True);
    }
}
