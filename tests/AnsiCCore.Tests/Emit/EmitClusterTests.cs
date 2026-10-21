namespace AnsiCCore.Tests.Emit;

/// <summary>
/// End-to-end tests for the "cheap, high-impact" coverage cluster: main(argc, argv),
/// shift-compound assignment on memory lvalues, value-context ++/-- on memory and bit-fields,
/// memmove, and the printf %X/%e/%g conversions.
/// </summary>
[TestFixture]
public class EmitClusterTests
{
    private static (int Exit, string Output) RunWithDriver(string cSource)
        => EmitTestHost.RunCSharpWithOutput(EmitTestHost.TranspileWithDriver(cSource, "p.c", [EmitTestHost.IncludeDir()]));

    // --- main(argc, argv) -------------------------------------------------

    [Test]
    public void Main_Argc_CountsArgsPlusProgramName()
    {
        // C# args omit the program name, so argc == args.Length + 1 (argv[0] is synthetic).
        const string src = "int main(int argc, char **argv) { return argc; }";
        Assert.That(EmitTestHost.RunWithArgs(src, "one", "two"), Is.EqualTo(3));
    }

    [Test]
    public void Main_Argv_IndexesIntoArguments()
    {
        // argv[0]="program", argv[1]="Z" -> argv[1][0] == 'Z' == 90.
        const string src = "int main(int argc, char **argv) { return argv[1][0]; }";
        Assert.That(EmitTestHost.RunWithArgs(src, "Z"), Is.EqualTo(90));
    }

    [Test]
    public void Main_Argv_ArrayParameterForm()
    {
        // char *argv[] adjusts to char ** — exercise the adjusted parameter path.
        const string src = """
            int main(int argc, char *argv[])
            {
                int total = 0;
                int i;
                for (i = 0; i < argc; i = i + 1)
                    total = total + argv[i][0];
                return total; /* 'p'(112) + 'a'(97) + 'b'(98) = 307, truncated to int fine */
            }
            """;
        Assert.That(EmitTestHost.RunWithArgs(src, "a", "b"), Is.EqualTo('p' + 'a' + 'b'));
    }

    [Test]
    public void Main_Argv_IsNullTerminated()
    {
        // C89 guarantees argv[argc] == NULL.
        const string src = """
            int main(int argc, char **argv)
            {
                return argv[argc] == 0 ? 42 : 0;
            }
            """;
        Assert.That(EmitTestHost.RunWithArgs(src, "x"), Is.EqualTo(42));
    }

    // --- <<= / >>= on memory lvalues --------------------------------------

    [Test]
    public void ShiftAssign_OnArrayElement()
    {
        // An array element is memory-backed, so this exercises the CompoundRhs path (not C#'s native <<=).
        const string src = "int main(void) { int a[1]; a[0] = 3; a[0] <<= 4; return a[0]; }"; // 3 << 4 = 48
        Assert.That(EmitTestHost.Run(src), Is.EqualTo(48));
    }

    [Test]
    public void ShiftAssign_RightOnPointerDeref()
    {
        const string src = "int main(void) { int x = 256; int *p = &x; *p >>= 2; return x; }"; // 256 >> 2 = 64
        Assert.That(EmitTestHost.Run(src), Is.EqualTo(64));
    }

    // --- value-context ++/-- on memory objects ----------------------------

    [Test]
    public void PostfixIncrement_OnMemoryObject_YieldsOldValue()
    {
        const string src = "int main(void) { int a[1]; a[0] = 5; int y = a[0]++; return y * 100 + a[0]; }"; // 5*100 + 6 = 506
        Assert.That(EmitTestHost.Run(src), Is.EqualTo(506));
    }

    [Test]
    public void PostfixDecrement_ThroughPointer_YieldsOldValue()
    {
        const string src = "int main(void) { int x = 9; int *p = &x; int y = (*p)--; return y * 100 + x; }"; // 9*100 + 8 = 908
        Assert.That(EmitTestHost.Run(src), Is.EqualTo(908));
    }

    [Test]
    public void PostfixIncrement_OnBitField_YieldsOldValue()
    {
        const string src = """
            struct Bits { unsigned f : 4; };
            int main(void)
            {
                struct Bits s;
                s.f = 5;
                {
                    int y = s.f++;
                    return y * 100 + s.f; /* 5*100 + 6 = 506 */
                }
            }
            """;
        Assert.That(EmitTestHost.Run(src), Is.EqualTo(506));
    }

    // --- memmove ----------------------------------------------------------

    [Test]
    public void Memmove_HandlesOverlappingForwardCopy()
    {
        // memmove must copy correctly even when regions overlap (unlike memcpy).
        const string src = """
            #include <string.h>
            #include <stdio.h>
            int main(void)
            {
                char s[8];
                strcpy(s, "abcdef");
                memmove(s + 1, s, 5); /* shift "abcde" right by one -> "aabcde" */
                printf("%s\n", s);
                return 0;
            }
            """;
        Assert.That(RunWithDriver(src).Output, Is.EqualTo("aabcde\n"));
    }

    // --- printf %X / %e / %g ----------------------------------------------

    [Test]
    public void Printf_UppercaseHex()
    {
        const string src = """
            #include <stdio.h>
            int main(void) { printf("%X %x\n", 3735928559u, 255); return 0; } /* DEADBEEF ff */
            """;
        Assert.That(RunWithDriver(src).Output, Is.EqualTo("DEADBEEF ff\n"));
    }

    [Test]
    public void Printf_Scientific()
    {
        // C uses a sign and at-least-two-digit exponent (31400 -> 3.140000e+04).
        const string src = """
            #include <stdio.h>
            int main(void) { printf("%e\n", 31400.0); return 0; }
            """;
        Assert.That(RunWithDriver(src).Output, Is.EqualTo("3.140000e+04\n"));
    }

    [Test]
    public void Printf_GeneralStripsTrailingZeros()
    {
        const string src = """
            #include <stdio.h>
            int main(void)
            {
                printf("%g %g %g %g\n", 100000.0, 1000000.0, 0.0001, 3.14159);
                return 0;
            }
            """;
        // %g: 100000 stays fixed; 1e+06 switches to exponent; 0.0001 fixed; pi to 6 sig digits.
        Assert.That(RunWithDriver(src).Output, Is.EqualTo("100000 1e+06 0.0001 3.14159\n"));
    }
}
