namespace AnsiCCore.Tests.Emit;

/// <summary>
/// End-to-end tests for the Tier-2 libc round-out: qsort/bsearch with user comparators, the
/// v*printf family forwarding a va_list, and errno (EDOM/ERANGE) set by strtol/strtod/math.
/// </summary>
[TestFixture]
public class EmitTier2LibcTests
{
    private static (int Exit, string Output) Run(string cSource)
        => EmitTestHost.RunCSharpWithOutput(EmitTestHost.TranspileWithDriver(cSource, "p.c", [EmitTestHost.IncludeDir()]));

    // --- qsort / bsearch --------------------------------------------------

    [Test]
    public void Qsort_SortsWithUserComparator()
    {
        const string src = """
            #include <stdlib.h>
            #include <stdio.h>
            int cmp(const void *a, const void *b)
            {
                int x = *(const int *)a, y = *(const int *)b;
                return (x > y) - (x < y);
            }
            int main(void)
            {
                int v[7] = { 5, 2, 8, 1, 9, 3, 7 };
                int i;
                qsort(v, 7, sizeof(int), cmp);
                for (i = 0; i < 7; i = i + 1) printf("%d", v[i]);
                printf("\n");
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("1235789\n"));
    }

    [Test]
    public void Bsearch_FindsAndMisses()
    {
        const string src = """
            #include <stdlib.h>
            #include <stdio.h>
            int cmp(const void *a, const void *b) { return *(const int *)a - *(const int *)b; }
            int main(void)
            {
                int v[6] = { 1, 3, 5, 7, 9, 11 };
                int k1 = 7, k2 = 8;
                int *f1 = (int *)bsearch(&k1, v, 6, sizeof(int), cmp);
                int *f2 = (int *)bsearch(&k2, v, 6, sizeof(int), cmp);
                printf("%d %d\n", f1 ? *f1 : -1, f2 ? *f2 : -1);
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("7 -1\n"));
    }

    [Test]
    public void Qsort_SortsStringsDescending()
    {
        // A comparator over char* elements, reversing strcmp order.
        const string src = """
            #include <stdlib.h>
            #include <string.h>
            #include <stdio.h>
            int rcmp(const void *a, const void *b)
            {
                return strcmp(*(const char * const *)b, *(const char * const *)a);
            }
            int main(void)
            {
                const char *words[4];
                words[0] = "banana"; words[1] = "apple"; words[2] = "cherry"; words[3] = "date";
                qsort(words, 4, sizeof(const char *), rcmp);
                { int i; for (i = 0; i < 4; i = i + 1) printf("%s ", words[i]); }
                printf("\n");
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("date cherry banana apple \n"));
    }

    // --- v*printf ---------------------------------------------------------

    [Test]
    public void Vprintf_ForwardsVaList()
    {
        const string src = """
            #include <stdio.h>
            #include <stdarg.h>
            void logline(const char *fmt, ...)
            {
                va_list ap;
                va_start(ap, fmt);
                vprintf(fmt, ap);
                va_end(ap);
            }
            int main(void) { logline("%d %s %.2f %c\n", 42, "hi", 3.14159, 'Z'); return 0; }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("42 hi 3.14 Z\n"));
    }

    [Test]
    public void Vsprintf_FormatsIntoBuffer()
    {
        const string src = """
            #include <stdio.h>
            #include <stdarg.h>
            int format(char *out, const char *fmt, ...)
            {
                va_list ap;
                int n;
                va_start(ap, fmt);
                n = vsprintf(out, fmt, ap);
                va_end(ap);
                return n;
            }
            int main(void)
            {
                char buf[32];
                int n = format(buf, "[%d-%d]", 7, 9);
                printf("%s len=%d\n", buf, n);
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("[7-9] len=5\n"));
    }

    [Test]
    public void Vfprintf_WritesToStderrStream()
    {
        const string src = """
            #include <stdio.h>
            #include <stdarg.h>
            void err(const char *fmt, ...)
            {
                va_list ap;
                va_start(ap, fmt);
                vfprintf(stdout, fmt, ap); /* stdout so the harness captures it */
                va_end(ap);
            }
            int main(void) { err("x=%d y=%d\n", 1, 2); return 0; }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("x=1 y=2\n"));
    }

    // --- errno ------------------------------------------------------------

    [Test]
    public void Errno_StrtolOverflowSetsERange()
    {
        const string src = """
            #include <errno.h>
            #include <stdlib.h>
            #include <stdio.h>
            int main(void)
            {
                errno = 0;
                long v = strtol("99999999999999999999999", 0, 10); /* overflow */
                printf("%d %d\n", errno == ERANGE ? 1 : 0, v == 9223372036854775807L ? 1 : 0);
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("1 1\n")); // ERANGE set, clamped to LONG_MAX
    }

    [Test]
    public void Errno_MathDomainAndRange()
    {
        const string src = """
            #include <errno.h>
            #include <math.h>
            #include <stdio.h>
            int main(void)
            {
                int domain;
                int range;
                errno = 0; sqrt(-1.0); domain = (errno == EDOM);
                errno = 0; exp(1000.0); range = (errno == ERANGE);
                printf("%d %d\n", domain, range);
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("1 1\n"));
    }

    [Test]
    public void Errno_NotSetOnSuccess()
    {
        const string src = """
            #include <errno.h>
            #include <math.h>
            #include <stdlib.h>
            #include <stdio.h>
            int main(void)
            {
                errno = 0;
                sqrt(4.0);
                strtol("42", 0, 10);
                printf("%d\n", errno);
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("0\n"));
    }
}
