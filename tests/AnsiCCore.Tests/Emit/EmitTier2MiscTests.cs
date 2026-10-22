namespace AnsiCCore.Tests.Emit;

/// <summary>
/// End-to-end tests for the rest of Tier 2: calendar &lt;time.h&gt; (gmtime/localtime/mktime/
/// asctime/strftime), stdio buffering/positioning (tmpfile/fflush/fgetpos/fsetpos/freopen),
/// &lt;locale.h&gt; (C locale), and the &lt;stdlib.h&gt; multibyte functions.
/// </summary>
[TestFixture]
public class EmitTier2MiscTests
{
    private static (int Exit, string Output) Run(string cSource)
        => EmitTestHost.RunCSharpWithOutput(EmitTestHost.TranspileWithDriver(cSource, "p.c", [EmitTestHost.IncludeDir()]));

    // --- calendar time ----------------------------------------------------

    [Test]
    public void Time_GmtimeStrftimeAsctime()
    {
        const string src = """
            #include <time.h>
            #include <stdio.h>
            int main(void)
            {
                time_t t = 1234567890; /* 2009-02-13 23:31:30 UTC, Friday */
                struct tm *g = gmtime(&t);
                char buf[80];
                strftime(buf, sizeof(buf), "%Y-%m-%d %H:%M:%S %a %A %b %B %j %p", g);
                printf("%s\n", buf);
                printf("%s", asctime(g));
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo(
            "2009-02-13 23:31:30 Fri Friday Feb February 044 PM\n" +
            "Fri Feb 13 23:31:30 2009\n"));
    }

    [Test]
    public void Time_MktimeRoundTripsThroughLocal()
    {
        // mktime interprets the fields as local time; localtime inverts it, so fields round-trip
        // regardless of the host timezone.
        const string src = """
            #include <time.h>
            #include <stdio.h>
            int main(void)
            {
                struct tm v;
                v.tm_year = 2009 - 1900; v.tm_mon = 5; v.tm_mday = 15;
                v.tm_hour = 12; v.tm_min = 30; v.tm_sec = 0; v.tm_isdst = -1;
                time_t t = mktime(&v);
                struct tm *lt = localtime(&t);
                printf("%d-%02d-%02d %02d:%02d wday=%d\n",
                    lt->tm_year + 1900, lt->tm_mon + 1, lt->tm_mday, lt->tm_hour, lt->tm_min, lt->tm_wday);
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("2009-06-15 12:30 wday=1\n")); // 2009-06-15 is a Monday
    }

    [Test]
    public void Time_MktimeNormalizesOutOfRangeFields()
    {
        const string src = """
            #include <time.h>
            #include <stdio.h>
            int main(void)
            {
                struct tm v;
                v.tm_year = 2000 - 1900; v.tm_mon = 0; v.tm_mday = 32; /* Jan 32 -> Feb 1 */
                v.tm_hour = 0; v.tm_min = 0; v.tm_sec = 0; v.tm_isdst = -1;
                mktime(&v);
                printf("%d-%02d-%02d\n", v.tm_year + 1900, v.tm_mon + 1, v.tm_mday);
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("2000-02-01\n"));
    }

    // --- stdio buffering / positioning ------------------------------------

    [Test]
    public void Stdio_TmpfileRoundTrip()
    {
        const string src = """
            #include <stdio.h>
            int main(void)
            {
                FILE *f = tmpfile();
                char buf[16];
                if (!f) return 99;
                fputs("hello", f);
                rewind(f);
                fgets(buf, sizeof(buf), f);
                fclose(f);
                printf("%s\n", buf);
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("hello\n"));
    }

    [Test]
    public void Stdio_FgetposFsetpos()
    {
        const string src = """
            #include <stdio.h>
            int main(void)
            {
                FILE *f = tmpfile();
                fpos_t pos;
                int c;
                fputs("ABCDEF", f);
                rewind(f);
                getc(f); getc(f);  /* now at index 2 */
                fgetpos(f, &pos);
                getc(f);           /* read 'C', now at 3 */
                fsetpos(f, &pos);  /* back to 2 */
                c = getc(f);       /* 'C' again */
                fclose(f);
                printf("%c\n", c);
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("C\n"));
    }

    [Test]
    public void Stdio_FflushDoesNotDisturbOutput()
    {
        const string src = """
            #include <stdio.h>
            int main(void) { printf("a"); fflush(stdout); printf("b\n"); return 0; }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("ab\n"));
    }

    [Test]
    public void Stdio_FreopenReusesStream()
    {
        var dir = Path.Combine(Path.GetTempPath(), "anscc_t2_" + Guid.NewGuid().ToString("N"));
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
                    fputs("first", f);
                    f = freopen("{{b}}", "w", f); /* same FILE*, now writing b */
                    if (!f) return 99;
                    fputs("second", f);
                    fclose(f);
                    return 0;
                }
                """;
            Assert.That(Run(src).Exit, Is.EqualTo(0));
            Assert.That(File.ReadAllText(a), Is.EqualTo("first"));
            Assert.That(File.ReadAllText(b), Is.EqualTo("second"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // --- locale -----------------------------------------------------------

    [Test]
    public void Locale_SetlocaleAndLocaleconv()
    {
        const string src = """
            #include <locale.h>
            #include <stdio.h>
            int main(void)
            {
                char *loc = setlocale(LC_ALL, "");
                struct lconv *lc = localeconv();
                printf("%s [%s] [%s]\n", loc, lc->decimal_point, lc->thousands_sep);
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("C [.] []\n"));
    }

    // --- multibyte --------------------------------------------------------

    [Test]
    public void Multibyte_RoundTrip()
    {
        const string src = """
            #include <stdlib.h>
            #include <stdio.h>
            int main(void)
            {
                wchar_t wbuf[8];
                char cbuf[8];
                int n = (int)mbstowcs(wbuf, "abc", 8);
                int m = (int)wcstombs(cbuf, wbuf, 8);
                cbuf[m] = 0;
                printf("%d %d %s %d\n", n, m, cbuf, mblen("x", 1));
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("3 3 abc 1\n"));
    }
}
