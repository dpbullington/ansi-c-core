using AnsiCCore.Runtime.Library;
using AnsiCCore.Runtime.Memory;

namespace AnsiCCore.Tests.Emit;

/// <summary>
/// A user-provided "library": precompiled .NET methods exported as C functions. A C program
/// declares them <c>extern</c> and the transpiler links calls to these methods at emit time —
/// the managed analog of linking against an object file.
/// </summary>
public static class DemoLibrary
{
    [CExport("triple")]
    public static int Triple(CMemory mem, int x) => x * 3;

    // Reads three ints from the passed array through the shared address space.
    [CExport("sum3")]
    public static int Sum3(CMemory mem, CPtr arr)
        => mem.ReadInt32(arr) + mem.ReadInt32(arr + 4) + mem.ReadInt32(arr + 8);

    // A variadic export: sums its (count) integer arguments.
    [CExport("sumv")]
    public static int SumV(CMemory mem, int count, params object[] args)
    {
        int total = 0;
        for (int i = 0; i < count; i++) total += Convert.ToInt32(args[i]);
        return total;
    }

    [CExport("scale")]
    public static double Scale(CMemory mem, double v, double factor) => v * factor;

    // Richer marshalling: a C char* arrives as a managed string, and a returned managed string
    // becomes a C char*.
    [CExport("greet")]
    public static string Greet(CMemory mem, string name) => "Hi " + name + "!";

    // C int arrives as a bool; a returned bool becomes a C int (0/1).
    [CExport("is_even")]
    public static bool IsEven(CMemory mem, int n) => n % 2 == 0;
}

[TestFixture]
public class LinkedExportTests
{
    private static int RunLinked(string cSource)
        => EmitTestHost.RunLinking(cSource, typeof(DemoLibrary).Assembly);

    [Test]
    public void ExternCall_LinksToExportedMethod()
    {
        const string src = "extern int triple(int);\nint main(void) { return triple(14); }";
        Assert.That(RunLinked(src), Is.EqualTo(42));
    }

    [Test]
    public void ExportedMethod_SharesTheAddressSpace()
    {
        // The exported sum3 reads the array the C program allocated.
        const string src = """
            extern int sum3(int *a);
            int main(void) {
                int a[3];
                a[0] = 10; a[1] = 20; a[2] = 12;
                return sum3(a);
            }
            """;
        Assert.That(RunLinked(src), Is.EqualTo(42));
    }

    [Test]
    public void VariadicExport_ReceivesBoxedArguments()
    {
        const string src = "extern int sumv(int count, ...);\nint main(void) { return sumv(4, 10, 11, 12, 9); }";
        Assert.That(RunLinked(src), Is.EqualTo(42));
    }

    [Test]
    public void ExportWithFloatingArguments()
    {
        const string src = "extern double scale(double v, double f);\nint main(void) { return (int)scale(10.5, 4.0); }";
        Assert.That(RunLinked(src), Is.EqualTo(42));
    }

    [Test]
    public void StringMarshalling_BothDirections()
    {
        // C passes a char* (managed string in), the export returns a managed string (char* out).
        const string src = """
            extern char *greet(const char *name);
            extern int puts(const char *s);
            int main(void) { puts(greet("Bob")); return 0; }
            """;
        var (_, output) = EmitTestHost.RunLinkingWithOutput(src, typeof(DemoLibrary).Assembly);
        Assert.That(output, Is.EqualTo("Hi Bob!\n"));
    }

    [Test]
    public void BoolReturnMarshalling()
    {
        const string src = "extern int is_even(int);\nint main(void) { return is_even(10) + is_even(7); }";
        Assert.That(RunLinked(src), Is.EqualTo(1)); // true(1) + false(0)
    }

    [Test]
    public void UserDefinedFunction_TakesPrecedenceOverExport()
    {
        // A program that defines triple itself must NOT be redirected to the library.
        const string src = "int triple(int x) { return x + 1; }\nint main(void) { return triple(41); }";
        Assert.That(RunLinked(src), Is.EqualTo(42));
    }
}
