using AnsiCCore.Runtime.Memory;

namespace AnsiCCore.Runtime.Library;

/// <summary>
/// &lt;math.h&gt; shims. These are pure (no <c>CMemory</c> parameter) and map directly to
/// <see cref="System.Math"/>. The constants <c>M_PI</c>/<c>M_E</c> are provided as macros by the
/// stub math.h.
/// </summary>
public static class CMath
{
    /// <summary><c>frexp</c>: splits x into a normalized mantissa in [0.5, 1) and an exponent (written through exp).</summary>
    [CExport("frexp")]
    public static double Frexp(CMemory mem, double x, CPtr exp)
    {
        if (x == 0 || double.IsNaN(x) || double.IsInfinity(x)) { mem.WriteInt32(exp, 0); return x; }
        int e = Math.ILogB(x) + 1;
        mem.WriteInt32(exp, e);
        return Math.ScaleB(x, -e);
    }

    /// <summary><c>modf</c>: splits x into integral (written through iptr) and fractional parts.</summary>
    [CExport("modf")]
    public static double Modf(CMemory mem, double x, CPtr iptr)
    {
        double integral = Math.Truncate(x);
        mem.WriteDouble(iptr, integral);
        return x - integral;
    }

    [CExport("sin")] public static double Sin(double x) => Math.Sin(x);
    [CExport("cos")] public static double Cos(double x) => Math.Cos(x);
    [CExport("tan")] public static double Tan(double x) => Math.Tan(x);
    [CExport("asin")] public static double Asin(CMemory mem, double x) => Check(mem, x, Math.Asin(x));
    [CExport("acos")] public static double Acos(CMemory mem, double x) => Check(mem, x, Math.Acos(x));
    [CExport("atan")] public static double Atan(double x) => Math.Atan(x);
    [CExport("atan2")] public static double Atan2(double y, double x) => Math.Atan2(y, x);

    [CExport("sinh")] public static double Sinh(double x) => Math.Sinh(x);
    [CExport("cosh")] public static double Cosh(double x) => Math.Cosh(x);
    [CExport("tanh")] public static double Tanh(double x) => Math.Tanh(x);

    [CExport("exp")] public static double Exp(CMemory mem, double x) => Check(mem, x, Math.Exp(x));
    [CExport("log")] public static double Log(CMemory mem, double x) => Check(mem, x, Math.Log(x));
    [CExport("log10")] public static double Log10(CMemory mem, double x) => Check(mem, x, Math.Log10(x));
    [CExport("pow")] public static double Pow(CMemory mem, double x, double y) => Check(mem, x, Math.Pow(x, y));
    [CExport("sqrt")] public static double Sqrt(CMemory mem, double x) => Check(mem, x, Math.Sqrt(x));

    /// <summary>Maps a math result to C89 errno (§7.5.1): a NaN from non-NaN input is a domain error
    /// (EDOM); an infinity from finite input is a range error (ERANGE).</summary>
    private static double Check(CMemory mem, double x, double result)
    {
        if (double.IsNaN(result) && !double.IsNaN(x)) CStdLib.SetErrno(mem, CStdLib.EDOM);
        else if (double.IsInfinity(result) && !double.IsInfinity(x)) CStdLib.SetErrno(mem, CStdLib.ERANGE);
        return result;
    }
    [CExport("cbrt")] public static double Cbrt(double x) => Math.Cbrt(x);

    [CExport("fabs")] public static double Fabs(double x) => Math.Abs(x);
    [CExport("fmod")] public static double Fmod(double x, double y) => x % y; // C# % on doubles matches C fmod
    [CExport("floor")] public static double Floor(double x) => Math.Floor(x);
    [CExport("ceil")] public static double Ceil(double x) => Math.Ceiling(x);
    [CExport("round")] public static double Round(double x) => Math.Round(x, MidpointRounding.AwayFromZero);
    [CExport("trunc")] public static double Trunc(double x) => Math.Truncate(x);

    [CExport("hypot")] public static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);
    [CExport("ldexp")] public static double Ldexp(double x, int exp) => Math.ScaleB(x, exp);
}
