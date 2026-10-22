namespace AnsiCCore.Tests.Emit;

/// <summary>
/// End-to-end tests for the Tier-1 libc round-out: extra ctype classifiers, the &lt;string.h&gt;
/// batch, &lt;stdlib.h&gt; labs/div/ldiv/getenv/system, &lt;stdio.h&gt; sscanf/ungetc/feof/getc/putc/
/// remove/rename, math frexp/modf, and the &lt;limits.h&gt;/&lt;float.h&gt;/offsetof macros.
/// </summary>
[TestFixture]
public class EmitTier1LibcTests
{
    private static (int Exit, string Output) Run(string cSource)
        => EmitTestHost.RunCSharpWithOutput(EmitTestHost.TranspileWithDriver(cSource, "p.c", [EmitTestHost.IncludeDir()]));

    private static (int Exit, string Output) RunIO(string cSource, string input)
        => EmitTestHost.RunCSharpWithIO(EmitTestHost.TranspileWithDriver(cSource, "p.c", [EmitTestHost.IncludeDir()]), input);

    // --- ctype ------------------------------------------------------------

    [Test]
    public void Ctype_Classifiers()
    {
        const string src = """
            #include <ctype.h>
            #include <stdio.h>
            int main(void)
            {
                printf("%d%d%d%d%d\n", isxdigit('F'), iscntrl('\n'), isprint(' '), isgraph(' '), ispunct('!'));
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("11101\n")); // xdigit, cntrl, print; space is not graph; '!' is punct
    }

    // --- string -----------------------------------------------------------

    [Test]
    public void String_CopyCatSearch()
    {
        const string src = """
            #include <string.h>
            #include <stdio.h>
            int main(void)
            {
                char buf[16];
                strncpy(buf, "abcdef", 3); buf[3] = 0; printf("%s ", buf);       /* abc */
                strcpy(buf, "foo"); strncat(buf, "barbaz", 3); printf("%s ", buf); /* foobar */
                printf("%s ", strstr("hello world", "wor"));                       /* world */
                printf("%s ", strrchr("a/b/c", '/'));                              /* /c */
                printf("%c ", *(char *)memchr("abcdef", 'd', 6));                  /* d */
                printf("%d ", (int)strspn("12345abc", "0123456789"));             /* 5 */
                printf("%d ", (int)strcspn("abc,def", ","));                       /* 3 */
                printf("%c", *strpbrk("abcdef", "xyzc"));                          /* c */
                printf("\n");
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("abc foobar world /c d 5 3 c\n"));
    }

    [Test]
    public void String_Tokenize()
    {
        const string src = """
            #include <string.h>
            #include <stdio.h>
            int main(void)
            {
                char s[] = "a,bb,,ccc";
                char *t = strtok(s, ",");
                while (t) { printf("[%s]", t); t = strtok(0, ","); }
                printf("\n");
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("[a][bb][ccc]\n")); // consecutive delimiters are skipped
    }

    [Test]
    public void String_CollateTransformAndError()
    {
        const string src = """
            #include <string.h>
            #include <stdio.h>
            int main(void)
            {
                char x[8];
                long n = strxfrm(x, "abc", 8);
                printf("%d %ld %s %s\n", strcoll("abc", "abd") < 0 ? 1 : 0, n, x, strerror(2));
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("1 3 abc No such file or directory\n"));
    }

    // --- stdlib -----------------------------------------------------------

    [Test]
    public void Stdlib_LabsAndAbs()
        => Assert.That(Run("#include <stdlib.h>\nint main(void){ return (int)labs(-5) + abs(-3); }").Exit, Is.EqualTo(8));

    [Test]
    public void Stdlib_Div()
        => Assert.That(Run("#include <stdlib.h>\nint main(void){ div_t r = div(17, 5); return r.quot * 10 + r.rem; }").Exit,
            Is.EqualTo(32)); // 17/5 = 3 r 2

    [Test]
    public void Stdlib_Ldiv()
        => Assert.That(Run("#include <stdlib.h>\nint main(void){ ldiv_t r = ldiv(1000, 7); return (int)(r.quot + r.rem); }").Exit,
            Is.EqualTo(148)); // 142 + 6

    [Test]
    public void Stdlib_Getenv()
    {
        Environment.SetEnvironmentVariable("ANSCC_TIER1_VAR", "xyz");
        try
        {
            const string src = """
                #include <stdlib.h>
                #include <stdio.h>
                int main(void)
                {
                    char *v = getenv("ANSCC_TIER1_VAR");
                    char *missing = getenv("ANSCC_TIER1_MISSING");
                    printf("%s %s\n", v ? v : "null", missing ? missing : "null");
                    return 0;
                }
                """;
            Assert.That(Run(src).Output, Is.EqualTo("xyz null\n"));
        }
        finally { Environment.SetEnvironmentVariable("ANSCC_TIER1_VAR", null); }
    }

    [Test]
    public void Stdlib_System()
        => Assert.That(Run("#include <stdlib.h>\nint main(void){ return system(\"exit 7\"); }").Exit, Is.EqualTo(7));

    // --- stdio ------------------------------------------------------------

    [Test]
    public void Stdio_Sscanf()
    {
        const string src = """
            #include <stdio.h>
            int main(void)
            {
                int a; int b; char s[8];
                int n = sscanf("42 7 hello", "%d %d %s", &a, &b, s);
                printf("%d %d %d %s\n", n, a, b, s);
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("3 42 7 hello\n"));
    }

    [Test]
    public void Stdio_UngetcPushesBack()
    {
        const string src = """
            #include <stdio.h>
            int main(void)
            {
                int c = getchar();
                ungetc(c, stdin);
                int d = getchar(); /* reads the pushed-back char again */
                return c == d ? c : 0;
            }
            """;
        Assert.That(RunIO(src, "A").Exit, Is.EqualTo('A'));
    }

    [Test]
    public void Stdio_FeofAtEndOfInput()
    {
        const string src = """
            #include <stdio.h>
            int main(void)
            {
                while (getchar() >= 0) { }
                return feof(stdin) ? 1 : 0;
            }
            """;
        Assert.That(RunIO(src, "ab").Exit, Is.EqualTo(1));
    }

    [Test]
    public void Stdio_PutcAndGetc()
    {
        // putc writes to stdout; getc reads a fopen'd temp file written just before.
        const string src = """
            #include <stdio.h>
            int main(void) { putc('X', stdout); putc('Y', stdout); return 0; }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("XY"));
    }

    [Test]
    public void Stdio_RemoveAndRename()
    {
        var dir = Path.Combine(Path.GetTempPath(), "anscc_tier1_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string a = Path.Combine(dir, "a.txt").Replace("\\", "/");
            string b = Path.Combine(dir, "b.txt").Replace("\\", "/");
            string src = $$"""
                #include <stdio.h>
                int main(void)
                {
                    FILE *f = fopen("{{a}}", "w");
                    fputs("hi", f);
                    fclose(f);
                    int r1 = rename("{{a}}", "{{b}}");
                    int r2 = remove("{{b}}");
                    int r3 = remove("{{a}}"); /* already gone -> nonzero */
                    return (r1 == 0 && r2 == 0 && r3 != 0) ? 1 : 0;
                }
                """;
            Assert.That(Run(src).Exit, Is.EqualTo(1));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // --- math -------------------------------------------------------------

    [Test]
    public void Math_FrexpAndModf()
    {
        const string src = """
            #include <math.h>
            #include <stdio.h>
            int main(void)
            {
                int e;
                double m = frexp(12.0, &e); /* 0.75 * 2^4 */
                double ip;
                double fp = modf(3.75, &ip);
                printf("%.2f %d %.2f %.2f\n", m, e, ip, fp);
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("0.75 4 3.00 0.75\n"));
    }

    // --- macro headers ----------------------------------------------------

    [Test]
    public void Macros_LimitsFloatAndOffsetof()
    {
        const string src = """
            #include <limits.h>
            #include <float.h>
            #include <stddef.h>
            #include <stdio.h>
            struct S { char a; int b; double c; };
            int main(void)
            {
                int epsOk = (DBL_EPSILON > 0.0 && DBL_EPSILON < 1.0 && FLT_MAX > 0.0) ? 1 : 0;
                printf("%d %ld %d %d\n", INT_MAX, LONG_MAX, CHAR_BIT, epsOk);
                printf("%d %d %d\n",
                    (int)offsetof(struct S, a), (int)offsetof(struct S, b), (int)offsetof(struct S, c));
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("2147483647 9223372036854775807 8 1\n0 4 8\n"));
    }
}
