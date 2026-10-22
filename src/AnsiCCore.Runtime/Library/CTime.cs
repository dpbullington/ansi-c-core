using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using AnsiCCore.Runtime.Memory;

namespace AnsiCCore.Runtime.Library;

/// <summary>
/// &lt;time.h&gt; shims. <c>time_t</c>/<c>clock_t</c> are modeled as <c>long</c>; <c>time</c> returns
/// Unix seconds and <c>clock</c> returns milliseconds (so the stub's CLOCKS_PER_SEC is 1000).
/// Broken-down time uses a nine-int <c>struct tm</c> laid out to match the stub &lt;time.h&gt;.
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

    // --- broken-down time (struct tm) -------------------------------------

    // struct tm layout: nine ints in the conventional order (must match the stub <time.h>).
    private const int TmSec = 0, TmMin = 4, TmHour = 8, TmMday = 12, TmMon = 16,
                      TmYear = 20, TmWday = 24, TmYday = 28, TmSize = 36;
    private const int AscLen = 32; // "Www Mmm dd hh:mm:ss yyyy\n\0" is 26; round up

    // Per-program static storage: slot 0 is the shared struct tm, slot 1 the asctime buffer.
    private static readonly ConditionalWeakTable<CMemory, CPtr[]> Statics = new();
    private static CPtr[] Buffers(CMemory mem)
        => Statics.GetValue(mem, m => new[] { m.Allocate(TmSize, RegionKind.Static), m.Allocate(AscLen, RegionKind.Static) });

    private static readonly string[] WDay = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] WDayFull = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    private static readonly string[] Mon = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    private static readonly string[] MonFull = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    [CExport("gmtime")]
    public static CPtr Gmtime(CMemory mem, CPtr timer)
    {
        var dt = DateTimeOffset.FromUnixTimeSeconds(mem.ReadInt64(timer)).UtcDateTime;
        var tm = Buffers(mem)[0];
        WriteTm(mem, tm, dt, 0);
        return tm;
    }

    [CExport("localtime")]
    public static CPtr Localtime(CMemory mem, CPtr timer)
    {
        var local = DateTimeOffset.FromUnixTimeSeconds(mem.ReadInt64(timer)).LocalDateTime;
        var tm = Buffers(mem)[0];
        WriteTm(mem, tm, local, local.IsDaylightSavingTime() ? 1 : 0);
        return tm;
    }

    [CExport("mktime")]
    public static long Mktime(CMemory mem, CPtr tm)
    {
        // Normalize by rebuilding from the fields and letting DateTime carry field overflow.
        var dt = new DateTime(mem.ReadInt32(tm + TmYear) + 1900, 1, 1, 0, 0, 0, DateTimeKind.Local)
            .AddMonths(mem.ReadInt32(tm + TmMon))
            .AddDays(mem.ReadInt32(tm + TmMday) - 1)
            .AddHours(mem.ReadInt32(tm + TmHour))
            .AddMinutes(mem.ReadInt32(tm + TmMin))
            .AddSeconds(mem.ReadInt32(tm + TmSec));
        WriteTm(mem, tm, dt, dt.IsDaylightSavingTime() ? 1 : 0); // write back the normalized fields
        return new DateTimeOffset(dt).ToUnixTimeSeconds();
    }

    [CExport("asctime")]
    public static CPtr Asctime(CMemory mem, CPtr tm)
    {
        // C89 §7.12.3.1 fixed form: "Www Mmm%3d %.2d:%.2d:%.2d %d\n".
        string s = string.Format(CultureInfo.InvariantCulture, "{0} {1}{2,3} {3:00}:{4:00}:{5:00} {6}\n",
            WDay[Wrap(mem.ReadInt32(tm + TmWday), 7)], Mon[Wrap(mem.ReadInt32(tm + TmMon), 12)],
            mem.ReadInt32(tm + TmMday), mem.ReadInt32(tm + TmHour),
            mem.ReadInt32(tm + TmMin), mem.ReadInt32(tm + TmSec), mem.ReadInt32(tm + TmYear) + 1900);
        return WriteString(mem, Buffers(mem)[1], s);
    }

    [CExport("ctime")]
    public static CPtr Ctime(CMemory mem, CPtr timer) => Asctime(mem, Localtime(mem, timer));

    [CExport("strftime")]
    public static long Strftime(CMemory mem, CPtr s, long max, CPtr format, CPtr tm)
    {
        int sec = mem.ReadInt32(tm + TmSec), min = mem.ReadInt32(tm + TmMin), hour = mem.ReadInt32(tm + TmHour);
        int mday = mem.ReadInt32(tm + TmMday), mon = Wrap(mem.ReadInt32(tm + TmMon), 12), year = mem.ReadInt32(tm + TmYear) + 1900;
        int wday = Wrap(mem.ReadInt32(tm + TmWday), 7), yday = mem.ReadInt32(tm + TmYday);
        string fmt = CMarshal.ToManaged(mem, format);
        var sb = new StringBuilder();

        for (int i = 0; i < fmt.Length; i++)
        {
            if (fmt[i] != '%' || i + 1 >= fmt.Length) { sb.Append(fmt[i]); continue; }
            switch (fmt[++i])
            {
                case 'Y': sb.Append(year); break;
                case 'y': sb.Append((year % 100).ToString("00", CultureInfo.InvariantCulture)); break;
                case 'm': sb.Append((mon + 1).ToString("00", CultureInfo.InvariantCulture)); break;
                case 'd': sb.Append(mday.ToString("00", CultureInfo.InvariantCulture)); break;
                case 'e': sb.Append(mday.ToString(CultureInfo.InvariantCulture).PadLeft(2)); break;
                case 'H': sb.Append(hour.ToString("00", CultureInfo.InvariantCulture)); break;
                case 'M': sb.Append(min.ToString("00", CultureInfo.InvariantCulture)); break;
                case 'S': sb.Append(sec.ToString("00", CultureInfo.InvariantCulture)); break;
                case 'j': sb.Append((yday + 1).ToString("000", CultureInfo.InvariantCulture)); break;
                case 'a': sb.Append(WDay[wday]); break;
                case 'A': sb.Append(WDayFull[wday]); break;
                case 'b': case 'h': sb.Append(Mon[mon]); break;
                case 'B': sb.Append(MonFull[mon]); break;
                case 'p': sb.Append(hour < 12 ? "AM" : "PM"); break;
                case 'w': sb.Append(wday); break;
                case '%': sb.Append('%'); break;
                default: sb.Append('%').Append(fmt[i]); break;
            }
        }

        string result = sb.ToString();
        if (max <= 0 || result.Length >= max) return 0; // must leave room for the terminating NUL
        WriteString(mem, s, result);
        return result.Length;
    }

    private static void WriteTm(CMemory mem, CPtr tm, DateTime dt, int isdst)
    {
        mem.WriteInt32(tm + TmSec, dt.Second);
        mem.WriteInt32(tm + TmMin, dt.Minute);
        mem.WriteInt32(tm + TmHour, dt.Hour);
        mem.WriteInt32(tm + TmMday, dt.Day);
        mem.WriteInt32(tm + TmMon, dt.Month - 1);
        mem.WriteInt32(tm + TmYear, dt.Year - 1900);
        mem.WriteInt32(tm + TmWday, (int)dt.DayOfWeek);
        mem.WriteInt32(tm + TmYday, dt.DayOfYear - 1);
        mem.WriteInt32(tm + 32, isdst); // tm_isdst
    }

    private static CPtr WriteString(CMemory mem, CPtr buffer, string text)
    {
        int i = 0;
        for (; i < text.Length; i++) mem.WriteUInt8(buffer + i, (byte)text[i]);
        mem.WriteUInt8(buffer + i, 0);
        return buffer;
    }

    private static int Wrap(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
