using AnsiCCore.Runtime.Memory;

namespace AnsiCCore.Runtime.Library;

/// <summary>
/// &lt;time.h&gt; shims. <c>time_t</c>/<c>clock_t</c> are modeled as <c>long</c>; <c>time</c> returns
/// Unix seconds and <c>clock</c> returns milliseconds (so the stub's CLOCKS_PER_SEC is 1000).
/// Calendar/broken-down time (struct tm, localtime, strftime) is not modeled yet.
/// </summary>
public static class CTime
{
    [CExport("time")]
    public static long Time(CMemory mem, CPtr t)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!t.IsNull) mem.WriteInt64(t, now);
        return now;
    }

    [CExport("clock")]
    public static long Clock(CMemory mem) => Environment.TickCount64; // milliseconds; CLOCKS_PER_SEC = 1000

    [CExport("difftime")]
    public static double Difftime(long end, long start) => end - start;
}
