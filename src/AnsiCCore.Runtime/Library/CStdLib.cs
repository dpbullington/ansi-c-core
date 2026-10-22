using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using AnsiCCore.Runtime.Memory;

namespace AnsiCCore.Runtime.Library;

/// <summary>
/// A minimal set of C standard-library shims the emitted code calls instead of linking libc.
/// Each takes the program's <see cref="CMemory"/> so string/heap operations act on the same
/// address space. This is intentionally a small, growing subset.
/// </summary>
public static class CStdLib
{
    // --- <ctype.h> (pure; no memory parameter) ----------------------------

    [CExport("isdigit")] public static int IsDigit(int c) => c is >= '0' and <= '9' ? 1 : 0;
    [CExport("isalpha")] public static int IsAlpha(int c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') ? 1 : 0;
    [CExport("isalnum")] public static int IsAlnum(int c) => IsDigit(c) != 0 || IsAlpha(c) != 0 ? 1 : 0;
    [CExport("isspace")] public static int IsSpace(int c) => c is ' ' or '\t' or '\n' or '\r' or '\v' or '\f' ? 1 : 0;
    [CExport("isupper")] public static int IsUpper(int c) => c is >= 'A' and <= 'Z' ? 1 : 0;
    [CExport("islower")] public static int IsLower(int c) => c is >= 'a' and <= 'z' ? 1 : 0;
    [CExport("isxdigit")] public static int IsXdigit(int c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F') ? 1 : 0;
    [CExport("iscntrl")] public static int IsCntrl(int c) => c is (>= 0 and <= 0x1f) or 0x7f ? 1 : 0;
    [CExport("isprint")] public static int IsPrint(int c) => c is >= 0x20 and <= 0x7e ? 1 : 0;
    [CExport("isgraph")] public static int IsGraph(int c) => c is >= 0x21 and <= 0x7e ? 1 : 0;
    [CExport("ispunct")] public static int IsPunct(int c) => IsGraph(c) != 0 && IsAlnum(c) == 0 ? 1 : 0;
    [CExport("toupper")] public static int ToUpper(int c) => c is >= 'a' and <= 'z' ? c - 32 : c;
    [CExport("tolower")] public static int ToLower(int c) => c is >= 'A' and <= 'Z' ? c + 32 : c;

    // --- <stdlib.h> -------------------------------------------------------

    [CExport("abs")] public static int Abs(int n) => n < 0 ? -n : n;

    [CExport("atoi")]
    public static int Atoi(CMemory mem, CPtr s)
    {
        long i = 0;
        while (IsSpace(mem.ReadUInt8(s + i)) != 0) i++;
        int sign = 1;
        byte c = mem.ReadUInt8(s + i);
        if (c is (byte)'+' or (byte)'-') { if (c == (byte)'-') sign = -1; i++; }
        int value = 0;
        while (IsDigit(c = mem.ReadUInt8(s + i)) != 0) { value = value * 10 + (c - '0'); i++; }
        return sign * value;
    }

    // --- <stdlib.h> numeric conversion ------------------------------------

    [CExport("atol")] public static long Atol(CMemory mem, CPtr s) => StrtoL(mem, s, CPtr.Null, 10);
    [CExport("atof")] public static double Atof(CMemory mem, CPtr s) => Strtod(mem, s, CPtr.Null);

    /// <summary>
    /// <c>strtol</c>: parses a long in <paramref name="radix"/> (0 auto-detects <c>0x</c>/<c>0</c>
    /// prefixes), skipping leading whitespace and an optional sign, clamping to the long range on
    /// overflow. When <paramref name="endptr"/> is non-null it receives the first unconsumed char.
    /// </summary>
    [CExport("strtol")]
    public static long StrtoL(CMemory mem, CPtr s, CPtr endptr, int radix)
    {
        ulong magnitude = ParseInteger(mem, s, radix, out bool negative, out long consumed, out bool any, out bool overflow);
        if (endptr != CPtr.Null) mem.WritePointer(endptr, s + (any ? consumed : 0));
        if (negative)
        {
            if (overflow || magnitude > 0x8000000000000000UL) { SetErrno(mem, ERANGE); return long.MinValue; }
            return unchecked(-(long)magnitude);
        }
        if (overflow || magnitude > long.MaxValue) { SetErrno(mem, ERANGE); return long.MaxValue; }
        return (long)magnitude;
    }

    /// <summary><c>strtoul</c>: like <see cref="StrtoL"/> but unsigned (a leading '-' negates modulo 2^64).</summary>
    [CExport("strtoul")]
    public static ulong StrtoUL(CMemory mem, CPtr s, CPtr endptr, int radix)
    {
        ulong magnitude = ParseInteger(mem, s, radix, out bool negative, out long consumed, out bool any, out bool overflow);
        if (endptr != CPtr.Null) mem.WritePointer(endptr, s + (any ? consumed : 0));
        if (overflow) { SetErrno(mem, ERANGE); return ulong.MaxValue; }
        return negative ? unchecked(0UL - magnitude) : magnitude;
    }

    /// <summary><c>strtod</c>: parses a double prefix (optional sign, digits, fraction, exponent).</summary>
    [CExport("strtod")]
    public static double Strtod(CMemory mem, CPtr s, CPtr endptr)
    {
        long i = 0;
        while (IsSpace(mem.ReadUInt8(s + i)) != 0) i++;
        var sb = new StringBuilder();

        byte c = mem.ReadUInt8(s + i);
        if (c is (byte)'+' or (byte)'-') { sb.Append((char)c); i++; }
        bool anyDigits = false;
        while (IsDigit(c = mem.ReadUInt8(s + i)) != 0) { sb.Append((char)c); i++; anyDigits = true; }
        if (mem.ReadUInt8(s + i) == (byte)'.')
        {
            sb.Append('.'); i++;
            while (IsDigit(c = mem.ReadUInt8(s + i)) != 0) { sb.Append((char)c); i++; anyDigits = true; }
        }
        if (anyDigits && (mem.ReadUInt8(s + i) is (byte)'e' or (byte)'E'))
        {
            long mark = i; var exp = new StringBuilder("e"); i++;
            byte sign = mem.ReadUInt8(s + i);
            if (sign is (byte)'+' or (byte)'-') { exp.Append((char)sign); i++; }
            bool expDigits = false;
            while (IsDigit(c = mem.ReadUInt8(s + i)) != 0) { exp.Append((char)c); i++; expDigits = true; }
            if (expDigits) sb.Append(exp); else i = mark; // a bare 'e' is not part of the number
        }

        if (!anyDigits) { if (endptr != CPtr.Null) mem.WritePointer(endptr, s); return 0.0; }
        if (endptr != CPtr.Null) mem.WritePointer(endptr, s + i);
        double value = double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0;
        if (double.IsInfinity(value)) SetErrno(mem, ERANGE); // out-of-range magnitude
        return value;
    }

    // --- <errno.h> --------------------------------------------------------

    public const int EDOM = 33;   // argument out of a function's domain
    public const int ERANGE = 34; // result out of range

    private static readonly ConditionalWeakTable<CMemory, CPtr[]> ErrnoCells = new();

    /// <summary>The address of the program's <c>errno</c> int; the &lt;errno.h&gt; macro dereferences it.</summary>
    [CExport("__errno_location")]
    public static CPtr ErrnoLocation(CMemory mem)
        => ErrnoCells.GetValue(mem, m => new[] { m.Allocate(4, RegionKind.Static) })[0];

    /// <summary>Sets <c>errno</c> for the program sharing <paramref name="mem"/> (used by library shims).</summary>
    public static void SetErrno(CMemory mem, int value) => mem.WriteInt32(ErrnoLocation(mem), value);

    /// <summary>Shared integer scanner for strtol/strtoul: returns the magnitude and parse metadata
    /// (<paramref name="overflow"/> is set if accumulation exceeded 64 bits).</summary>
    private static ulong ParseInteger(CMemory mem, CPtr s, int radix, out bool negative, out long consumed, out bool any, out bool overflow)
    {
        long i = 0;
        while (IsSpace(mem.ReadUInt8(s + i)) != 0) i++;

        negative = false;
        byte c = mem.ReadUInt8(s + i);
        if (c is (byte)'+' or (byte)'-') { negative = c == (byte)'-'; i++; }

        if (radix == 0)
        {
            if (mem.ReadUInt8(s + i) == (byte)'0' && (mem.ReadUInt8(s + i + 1) is (byte)'x' or (byte)'X')) { radix = 16; i += 2; }
            else if (mem.ReadUInt8(s + i) == (byte)'0') { radix = 8; }
            else radix = 10;
        }
        else if (radix == 16 && mem.ReadUInt8(s + i) == (byte)'0' && (mem.ReadUInt8(s + i + 1) is (byte)'x' or (byte)'X'))
        {
            i += 2;
        }

        ulong value = 0;
        any = false;
        overflow = false;
        int digit;
        while ((digit = DigitValue(mem.ReadUInt8(s + i))) >= 0 && digit < radix)
        {
            if (value > (ulong.MaxValue - (ulong)digit) / (ulong)radix) overflow = true;
            value = value * (ulong)radix + (ulong)digit;
            i++; any = true;
        }
        consumed = i;
        return value;
    }

    private static int DigitValue(byte c) => c switch
    {
        >= (byte)'0' and <= (byte)'9' => c - '0',
        >= (byte)'a' and <= (byte)'z' => c - 'a' + 10,
        >= (byte)'A' and <= (byte)'Z' => c - 'A' + 10,
        _ => -1,
    };

    // --- <stdlib.h> pseudo-random -----------------------------------------

    // Per-program RNG state (C89 initial seed is 1), isolated by CMemory like the stream table.
    private static readonly ConditionalWeakTable<CMemory, long[]> RandState = new();
    private static long[] Seed(CMemory mem) => RandState.GetValue(mem, _ => new long[] { 1 });

    /// <summary><c>rand</c>: the classic portable LCG, yielding values in [0, 32767] (RAND_MAX).</summary>
    [CExport("rand")]
    public static int Rand(CMemory mem)
    {
        var state = Seed(mem);
        state[0] = unchecked(state[0] * 1103515245 + 12345);
        return (int)((ulong)state[0] >> 16) & 0x7fff;
    }

    [CExport("srand")]
    public static void Srand(CMemory mem, uint seed) => Seed(mem)[0] = seed;

    // --- <stdlib.h> integer arithmetic & environment ----------------------

    [CExport("labs")] public static long Labs(long n) => n < 0 ? -n : n;

    // div_t / ldiv_t are returned by value: {quot, rem}. The emitter treats a struct return as a
    // CPtr to its storage, so allocate the pair and let the matching struct layout read it back.
    [CExport("div")]
    public static CPtr Div(CMemory mem, int numer, int denom)
    {
        var r = mem.Allocate(8, RegionKind.Static);
        mem.WriteInt32(r, numer / denom);
        mem.WriteInt32(r + 4, numer % denom);
        return r;
    }

    [CExport("ldiv")]
    public static CPtr Ldiv(CMemory mem, long numer, long denom)
    {
        var r = mem.Allocate(16, RegionKind.Static);
        mem.WriteInt64(r, numer / denom);
        mem.WriteInt64(r + 8, numer % denom);
        return r;
    }

    [CExport("getenv")]
    public static CPtr Getenv(CMemory mem, CPtr name)
    {
        string? value = Environment.GetEnvironmentVariable(ReadCString(mem, name));
        return value is null ? CPtr.Null : CMarshal.FromManaged(mem, value);
    }

    /// <summary>
    /// <c>qsort</c>: sorts <paramref name="nmemb"/> elements of <paramref name="size"/> bytes in
    /// place. The comparator arrives as a managed delegate (a C function pointer passed by name).
    /// Indices are sorted over the unmoving original elements so the comparator's pointers stay
    /// valid throughout, then the permutation is materialized back into the array.
    /// </summary>
    [CExport("qsort")]
    public static void Qsort(CMemory mem, CPtr @base, long nmemb, long size, Func<CPtr, CPtr, int> compar)
    {
        if (nmemb <= 1 || size <= 0) return;
        var index = new int[nmemb];
        for (int i = 0; i < nmemb; i++) index[i] = i;
        Array.Sort(index, (x, y) => compar(@base + (long)x * size, @base + (long)y * size));

        var scratch = mem.Allocate(nmemb * size, RegionKind.Automatic);
        for (int i = 0; i < nmemb; i++) mem.Copy(scratch + (long)i * size, @base + (long)index[i] * size, size);
        mem.Copy(@base, scratch, nmemb * size);
    }

    /// <summary><c>bsearch</c>: binary-searches a sorted array, returning a pointer to a matching
    /// element or null. The comparator is called as <c>compar(key, element)</c>.</summary>
    [CExport("bsearch")]
    public static CPtr Bsearch(CMemory mem, CPtr key, CPtr @base, long nmemb, long size, Func<CPtr, CPtr, int> compar)
    {
        long lo = 0, hi = nmemb;
        while (lo < hi)
        {
            long mid = (lo + hi) / 2;
            CPtr elem = @base + mid * size;
            int c = compar(key, elem);
            if (c == 0) return elem;
            if (c < 0) hi = mid; else lo = mid + 1;
        }
        return CPtr.Null;
    }

    /// <summary><c>system</c>: runs the command through the platform shell, returning its exit code
    /// (a null command reports that a command processor is available).</summary>
    [CExport("system")]
    public static int System(CMemory mem, CPtr command)
    {
        if (command == CPtr.Null) return 1;
        bool windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        var psi = new global::System.Diagnostics.ProcessStartInfo { FileName = windows ? "cmd.exe" : "/bin/sh", UseShellExecute = false };
        psi.ArgumentList.Add(windows ? "/c" : "-c");
        psi.ArgumentList.Add(ReadCString(mem, command));
        try
        {
            using var p = global::System.Diagnostics.Process.Start(psi);
            if (p is null) return -1;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Exception ex) when (ex is global::System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return -1;
        }
    }

    // --- <stdlib.h> heap --------------------------------------------------

    [CExport("malloc")] public static CPtr Malloc(CMemory mem, long size) => mem.Malloc(size);
    [CExport("calloc")] public static CPtr Calloc(CMemory mem, long count, long size) => mem.Calloc(count, size);
    [CExport("realloc")] public static CPtr Realloc(CMemory mem, CPtr ptr, long size) => mem.Realloc(ptr, size);
    [CExport("free")] public static void Free(CMemory mem, CPtr ptr) => mem.Free(ptr);

    // --- <string.h> -------------------------------------------------------

    [CExport("memcpy")] public static CPtr Memcpy(CMemory mem, CPtr dst, CPtr src, long n) { mem.Copy(dst, src, n); return dst; }
    [CExport("memmove")] public static CPtr Memmove(CMemory mem, CPtr dst, CPtr src, long n) { mem.Copy(dst, src, n); return dst; }
    [CExport("memset")] public static CPtr Memset(CMemory mem, CPtr dst, int value, long n) { mem.Set(dst, unchecked((byte)value), n); return dst; }
    [CExport("memcmp")] public static int Memcmp(CMemory mem, CPtr a, CPtr b, long n) => Math.Sign(mem.Compare(a, b, n));

    [CExport("strlen")]
    public static int Strlen(CMemory mem, CPtr s)
    {
        int n = 0;
        while (mem.ReadUInt8(s + n) != 0) n++;
        return n;
    }

    [CExport("strcmp")]
    public static int Strcmp(CMemory mem, CPtr a, CPtr b)
    {
        for (long i = 0; ; i++)
        {
            byte ca = mem.ReadUInt8(a + i), cb = mem.ReadUInt8(b + i);
            if (ca != cb) return ca < cb ? -1 : 1;
            if (ca == 0) return 0;
        }
    }

    [CExport("strncmp")]
    public static int Strncmp(CMemory mem, CPtr a, CPtr b, long n)
    {
        for (long i = 0; i < n; i++)
        {
            byte ca = mem.ReadUInt8(a + i), cb = mem.ReadUInt8(b + i);
            if (ca != cb) return ca < cb ? -1 : 1;
            if (ca == 0) return 0;
        }
        return 0;
    }

    [CExport("strchr")]
    public static CPtr Strchr(CMemory mem, CPtr s, int c)
    {
        for (long i = 0; ; i++)
        {
            byte ch = mem.ReadUInt8(s + i);
            if (ch == unchecked((byte)c)) return s + i;
            if (ch == 0) return CPtr.Null;
        }
    }

    [CExport("strcat")]
    public static CPtr Strcat(CMemory mem, CPtr dst, CPtr src)
    {
        long end = Strlen(mem, dst);
        long i = 0;
        byte c;
        do { c = mem.ReadUInt8(src + i); mem.WriteUInt8(dst + end + i, c); i++; } while (c != 0);
        return dst;
    }

    [CExport("strcpy")]
    public static CPtr Strcpy(CMemory mem, CPtr dst, CPtr src)
    {
        long i = 0;
        byte c;
        do { c = mem.ReadUInt8(src + i); mem.WriteUInt8(dst + i, c); i++; } while (c != 0);
        return dst;
    }

    [CExport("strncpy")]
    public static CPtr Strncpy(CMemory mem, CPtr dst, CPtr src, long n)
    {
        long i = 0;
        while (i < n) { byte c = mem.ReadUInt8(src + i); mem.WriteUInt8(dst + i, c); i++; if (c == 0) break; }
        while (i < n) { mem.WriteUInt8(dst + i, 0); i++; } // pad the remainder with NUL (C89 §7.11.2.4)
        return dst;
    }

    [CExport("strncat")]
    public static CPtr Strncat(CMemory mem, CPtr dst, CPtr src, long n)
    {
        long end = Strlen(mem, dst), i = 0;
        for (; i < n; i++) { byte c = mem.ReadUInt8(src + i); if (c == 0) break; mem.WriteUInt8(dst + end + i, c); }
        mem.WriteUInt8(dst + end + i, 0);
        return dst;
    }

    [CExport("strrchr")]
    public static CPtr Strrchr(CMemory mem, CPtr s, int c)
    {
        byte target = unchecked((byte)c);
        CPtr last = CPtr.Null;
        for (long i = 0; ; i++)
        {
            byte ch = mem.ReadUInt8(s + i);
            if (ch == target) last = s + i;
            if (ch == 0) return last; // matches the terminator too when c == 0
        }
    }

    [CExport("strstr")]
    public static CPtr Strstr(CMemory mem, CPtr hay, CPtr needle)
    {
        if (mem.ReadUInt8(needle) == 0) return hay; // empty needle matches at the start
        for (long i = 0; mem.ReadUInt8(hay + i) != 0; i++)
            for (long j = 0; ; j++)
            {
                byte nc = mem.ReadUInt8(needle + j);
                if (nc == 0) return hay + i; // whole needle matched
                if (mem.ReadUInt8(hay + i + j) != nc) break;
            }
        return CPtr.Null;
    }

    [CExport("memchr")]
    public static CPtr Memchr(CMemory mem, CPtr s, int c, long n)
    {
        byte target = unchecked((byte)c);
        for (long i = 0; i < n; i++) if (mem.ReadUInt8(s + i) == target) return s + i;
        return CPtr.Null;
    }

    [CExport("strspn")]
    public static long Strspn(CMemory mem, CPtr s, CPtr accept)
    {
        long i = 0;
        byte c;
        while ((c = mem.ReadUInt8(s + i)) != 0 && InSet(mem, accept, c)) i++;
        return i;
    }

    [CExport("strcspn")]
    public static long Strcspn(CMemory mem, CPtr s, CPtr reject)
    {
        long i = 0;
        byte c;
        while ((c = mem.ReadUInt8(s + i)) != 0 && !InSet(mem, reject, c)) i++;
        return i;
    }

    [CExport("strpbrk")]
    public static CPtr Strpbrk(CMemory mem, CPtr s, CPtr accept)
    {
        for (long i = 0; ; i++)
        {
            byte c = mem.ReadUInt8(s + i);
            if (c == 0) return CPtr.Null;
            if (InSet(mem, accept, c)) return s + i;
        }
    }

    // strtok keeps a per-program saved position between calls (C89 §7.11.5.8).
    private static readonly ConditionalWeakTable<CMemory, CPtr[]> StrtokSaved = new();

    [CExport("strtok")]
    public static CPtr Strtok(CMemory mem, CPtr s, CPtr delim)
    {
        var saved = StrtokSaved.GetValue(mem, _ => new[] { CPtr.Null });
        CPtr cur = s != CPtr.Null ? s : saved[0];
        if (cur == CPtr.Null) return CPtr.Null;

        byte c;
        while ((c = mem.ReadUInt8(cur)) != 0 && InSet(mem, delim, c)) cur += 1; // skip leading delimiters
        if (c == 0) { saved[0] = CPtr.Null; return CPtr.Null; }

        CPtr token = cur;
        while ((c = mem.ReadUInt8(cur)) != 0 && !InSet(mem, delim, c)) cur += 1;
        if (c != 0) { mem.WriteUInt8(cur, 0); saved[0] = cur + 1; } else saved[0] = CPtr.Null;
        return token;
    }

    [CExport("strcoll")] public static int Strcoll(CMemory mem, CPtr a, CPtr b) => Strcmp(mem, a, b); // "C" locale

    [CExport("strxfrm")]
    public static long Strxfrm(CMemory mem, CPtr dst, CPtr src, long n)
    {
        long len = Strlen(mem, src); // "C" locale: the transform is an identity copy
        if (n > 0)
        {
            long copy = Math.Min(len, n - 1);
            for (long i = 0; i < copy; i++) mem.WriteUInt8(dst + i, mem.ReadUInt8(src + i));
            mem.WriteUInt8(dst + copy, 0);
        }
        return len;
    }

    [CExport("strerror")]
    public static CPtr Strerror(CMemory mem, int errnum) => CMarshal.FromManaged(mem, errnum switch
    {
        0 => "No error",
        1 => "Operation not permitted",
        2 => "No such file or directory",
        13 => "Permission denied",
        22 => "Invalid argument",
        33 => "Numerical argument out of domain",  // EDOM
        34 => "Numerical result out of range",      // ERANGE
        _ => "Unknown error",
    });

    /// <summary>True if byte <paramref name="c"/> appears in the NUL-terminated set at <paramref name="set"/>.</summary>
    private static bool InSet(CMemory mem, CPtr set, byte c)
    {
        for (long i = 0; ; i++)
        {
            byte s = mem.ReadUInt8(set + i);
            if (s == 0) return false;
            if (s == c) return true;
        }
    }

    // --- <stdio.h> streams ------------------------------------------------
    //
    // FILE* is a sentinel pointer. The stub stdio.h defines stdin/stdout/stderr as
    // ((FILE*)3/1/2); fopen allocates further sentinels. A per-CMemory registry maps each
    // sentinel to a CFile, so standard streams honor Console redirection and files are real.

    private sealed class CFile
    {
        public enum Std { None, In, Out, Err }
        public Std Standard;
        public Stream? Stream;             // backing file (null for the standard streams)
        public bool Eof;                   // set when a read hits end-of-input (feof)
        public bool Error;                 // set on a stream error (ferror)
        private const int Empty = -2;
        private int _peek = Empty;         // one-byte lookahead for files (text parsing)
        private int _unget = Empty;        // one-char ungetc pushback (any stream kind)

        public int Peek()
        {
            if (_unget != Empty) return _unget;
            if (Standard == Std.In) return Console.In.Peek();
            if (Stream is null) return -1;
            if (_peek == Empty) _peek = Stream.ReadByte();
            return _peek;
        }

        public int Read()
        {
            if (_unget != Empty) { int v = _unget; _unget = Empty; return v; } // pushback is never EOF
            int r;
            if (Standard == Std.In) r = Console.In.Read();
            else if (Stream is null) r = -1;
            else if (_peek != Empty) { r = _peek; _peek = Empty; }
            else r = Stream.ReadByte();
            if (r < 0) Eof = true;
            return r;
        }

        /// <summary>Pushes one character back onto the stream (C89 §7.9.7.11 guarantees one level).</summary>
        public bool Ungetc(int c)
        {
            if (c < 0 || _unget != Empty) return false;
            _unget = c;
            Eof = false;
            return true;
        }

        public void WriteByte(byte b)
        {
            if (Standard == Std.Out) Console.Out.Write((char)b);
            else if (Standard == Std.Err) Console.Error.Write((char)b);
            else Stream?.WriteByte(b);
        }

        public void WriteText(string s)
        {
            if (Standard == Std.Out) Console.Out.Write(s);
            else if (Standard == Std.Err) Console.Error.Write(s);
            else if (Stream is not null) foreach (char c in s) Stream.WriteByte((byte)c);
        }

        public long Position => Stream is null ? 0 : Stream.Position - (_peek == Empty ? 0 : 1);

        public bool Seek(long offset, SeekOrigin origin)
        {
            if (Stream is null) return false;
            _peek = Empty;
            Stream.Seek(offset, origin);
            return true;
        }

        public void Dispose() { Stream?.Flush(); Stream?.Dispose(); }
    }

    private sealed class StreamTable
    {
        public readonly Dictionary<ulong, CFile> Files = new()
        {
            [1] = new CFile { Standard = CFile.Std.Out },
            [2] = new CFile { Standard = CFile.Std.Err },
            [3] = new CFile { Standard = CFile.Std.In },
        };
        public ulong Next = 0x100; // fopen sentinels, kept below the 0x1000 memory base
    }

    private static readonly ConditionalWeakTable<CMemory, StreamTable> Streams = new();
    private static StreamTable Table(CMemory mem) => Streams.GetValue(mem, _ => new StreamTable());

    private static CFile Handle(CMemory mem, CPtr stream)
    {
        var table = Table(mem);
        return table.Files.GetValueOrDefault(stream.Address) ?? table.Files[1];
    }

    [CExport("fopen")]
    public static CPtr Fopen(CMemory mem, CPtr path, CPtr mode)
    {
        string file = ReadCString(mem, path), m = ReadCString(mem, mode);
        try
        {
            (FileMode fileMode, FileAccess access) = m.Length == 0 ? default : m[0] switch
            {
                'r' => (FileMode.Open, m.Contains('+') ? FileAccess.ReadWrite : FileAccess.Read),
                'w' => (FileMode.Create, m.Contains('+') ? FileAccess.ReadWrite : FileAccess.Write),
                'a' => (FileMode.Append, FileAccess.Write),
                _ => throw new IOException($"unsupported fopen mode '{m}'"),
            };

            var table = Table(mem);
            ulong address = table.Next++;
            table.Files[address] = new CFile { Stream = new FileStream(file, fileMode, access) };
            return new CPtr(address);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return CPtr.Null;
        }
    }

    [CExport("fclose")]
    public static int Fclose(CMemory mem, CPtr stream)
    {
        var table = Table(mem);
        if (!table.Files.TryGetValue(stream.Address, out var handle) || handle.Standard != CFile.Std.None)
            return -1;
        handle.Dispose();
        table.Files.Remove(stream.Address);
        return 0;
    }

    // --- <stdio.h> binary I/O --------------------------------------------

    [CExport("fread")]
    public static long Fread(CMemory mem, CPtr ptr, long size, long count, CPtr stream)
    {
        var f = Handle(mem, stream);
        long total = size * count, read = 0;
        for (; read < total; read++)
        {
            int b = f.Read();
            if (b < 0) break;
            mem.WriteUInt8(ptr + read, (byte)b);
        }
        return size == 0 ? 0 : read / size; // number of complete elements
    }

    [CExport("fwrite")]
    public static long Fwrite(CMemory mem, CPtr ptr, long size, long count, CPtr stream)
    {
        var f = Handle(mem, stream);
        long total = size * count;
        for (long i = 0; i < total; i++) f.WriteByte(mem.ReadUInt8(ptr + i));
        return count;
    }

    [CExport("fseek")]
    public static int Fseek(CMemory mem, CPtr stream, long offset, int whence)
    {
        var origin = whence == 1 ? SeekOrigin.Current : whence == 2 ? SeekOrigin.End : SeekOrigin.Begin;
        return Handle(mem, stream).Seek(offset, origin) ? 0 : -1;
    }

    [CExport("ftell")]
    public static long Ftell(CMemory mem, CPtr stream) => Handle(mem, stream).Position;

    [CExport("rewind")]
    public static void Rewind(CMemory mem, CPtr stream) => Handle(mem, stream).Seek(0, SeekOrigin.Begin);

    // --- <stdio.h> output -------------------------------------------------

    [CExport("putchar")]
    public static int Putchar(CMemory mem, int c)
    {
        Console.Out.Write((char)(byte)c);
        return c & 0xFF;
    }

    [CExport("puts")]
    public static int Puts(CMemory mem, CPtr s)
    {
        Console.Out.Write(ReadCString(mem, s));
        Console.Out.Write('\n');
        return 0;
    }

    [CExport("fputc")]
    public static int Fputc(CMemory mem, int c, CPtr stream)
    {
        Handle(mem, stream).WriteByte((byte)c);
        return c & 0xFF;
    }

    [CExport("fputs")]
    public static int Fputs(CMemory mem, CPtr s, CPtr stream)
    {
        Handle(mem, stream).WriteText(ReadCString(mem, s));
        return 0;
    }

    [CExport("printf")]
    public static int Printf(CMemory mem, CPtr format, params object[] args)
    {
        string output = Format(mem, format, new BoxedArgs(args));
        Console.Out.Write(output);
        return output.Length;
    }

    [CExport("fprintf")]
    public static int Fprintf(CMemory mem, CPtr stream, CPtr format, params object[] args)
    {
        string output = Format(mem, format, new BoxedArgs(args));
        Handle(mem, stream).WriteText(output);
        return output.Length;
    }

    [CExport("sprintf")]
    public static int Sprintf(CMemory mem, CPtr buffer, CPtr format, params object[] args)
        => WriteCString(mem, buffer, Format(mem, format, new BoxedArgs(args)));

    // The v*printf family takes a va_list (a cursor into the packed vararg region) instead of a
    // boxed array; the formatter reads each slot as the active conversion requires.
    [CExport("vprintf")]
    public static int Vprintf(CMemory mem, CPtr format, CPtr ap)
    {
        string output = Format(mem, format, new VaListArgs(mem, ap));
        Console.Out.Write(output);
        return output.Length;
    }

    [CExport("vfprintf")]
    public static int Vfprintf(CMemory mem, CPtr stream, CPtr format, CPtr ap)
    {
        string output = Format(mem, format, new VaListArgs(mem, ap));
        Handle(mem, stream).WriteText(output);
        return output.Length;
    }

    [CExport("vsprintf")]
    public static int Vsprintf(CMemory mem, CPtr buffer, CPtr format, CPtr ap)
        => WriteCString(mem, buffer, Format(mem, format, new VaListArgs(mem, ap)));

    private static int WriteCString(CMemory mem, CPtr buffer, string output)
    {
        for (int i = 0; i < output.Length; i++) mem.WriteUInt8(buffer + i, (byte)output[i]);
        mem.WriteUInt8(buffer + output.Length, 0);
        return output.Length;
    }

    /// <summary>Supplies printf arguments in order, backed either by a boxed array (printf) or a
    /// va_list region (vprintf). Each accessor reads the value the active conversion needs.</summary>
    private interface IPrintfArgs
    {
        long NextI64();
        ulong NextU64();
        double NextF64();
        CPtr NextPtr();
        int NextI32();
    }

    private sealed class BoxedArgs(object[] args) : IPrintfArgs
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private int _i;
        public long NextI64() => Convert.ToInt64(args[_i++], Inv);
        public ulong NextU64() => Convert.ToUInt64(args[_i++], Inv);
        public double NextF64() => Convert.ToDouble(args[_i++], Inv);
        public CPtr NextPtr() => (CPtr)args[_i++];
        public int NextI32() => Convert.ToInt32(args[_i++], Inv);
    }

    private sealed class VaListArgs(CMemory mem, CPtr p) : IPrintfArgs
    {
        private CPtr _p = p;
        private CPtr Take() { var c = _p; _p += CVarArgs.SlotSize; return c; } // one 8-byte slot per arg
        public long NextI64() => mem.ReadInt64(Take());
        public ulong NextU64() => mem.ReadUInt64(Take());
        public double NextF64() => mem.ReadDouble(Take());
        public CPtr NextPtr() => mem.ReadPointer(Take());
        public int NextI32() => unchecked((int)mem.ReadInt64(Take()));
    }

    /// <summary>
    /// The shared <c>printf</c>-family formatter: supports <c>%d %i %u %o %x %X %p %c %s %f %e %E %g
    /// %G %%</c> with the <c>- + space 0</c> flags, field width, and precision (and tolerated length
    /// modifiers). Arguments are pulled in declaration order from <paramref name="args"/>.
    /// </summary>
    private static string Format(CMemory mem, CPtr format, IPrintfArgs args)
    {
        var inv = CultureInfo.InvariantCulture;
        string fmt = ReadCString(mem, format);
        var sb = new StringBuilder();
        int i = 0;

        while (i < fmt.Length)
        {
            char c = fmt[i++];
            if (c != '%') { sb.Append(c); continue; }

            bool left = false, zero = false, plus = false, space = false;
            for (; i < fmt.Length; i++)
            {
                char fl = fmt[i];
                if (fl == '-') left = true;
                else if (fl == '0') zero = true;
                else if (fl == '+') plus = true;
                else if (fl == ' ') space = true;
                else if (fl == '#') { }
                else break;
            }

            int width = 0;
            if (i < fmt.Length && fmt[i] == '*') // width taken from an argument
            {
                i++;
                width = args.NextI32();
                if (width < 0) { left = true; width = -width; }
            }
            else
            {
                while (i < fmt.Length && char.IsAsciiDigit(fmt[i])) width = width * 10 + (fmt[i++] - '0');
            }

            int precision = -1;
            if (i < fmt.Length && fmt[i] == '.')
            {
                i++;
                if (i < fmt.Length && fmt[i] == '*') { i++; precision = Math.Max(0, args.NextI32()); }
                else { precision = 0; while (i < fmt.Length && char.IsAsciiDigit(fmt[i])) precision = precision * 10 + (fmt[i++] - '0'); }
            }

            while (i < fmt.Length && "lhLzjt".IndexOf(fmt[i]) >= 0) i++;
            if (i >= fmt.Length) break;
            char conv = fmt[i++];

            string text;
            switch (conv)
            {
                case '%': sb.Append('%'); continue;
                case 'd': case 'i': text = FormatSigned(args.NextI64(), plus, space, precision); break;
                case 'u': text = Pad0(args.NextU64().ToString(inv), precision); break;
                case 'x': text = Pad0(args.NextU64().ToString("x", inv), precision); break;
                case 'X': text = Pad0(args.NextU64().ToString("X", inv), precision); break;
                case 'o': text = Pad0(ToOctal(args.NextU64()), precision); break;
                case 'p': text = "0x" + args.NextPtr().Address.ToString("x", inv); break;
                case 'f': case 'F': text = FormatFloat(args.NextF64(), precision < 0 ? 6 : precision, plus, space); break;
                case 'e': case 'E': text = FormatScientific(args.NextF64(), precision < 0 ? 6 : precision, conv == 'E', plus, space); break;
                case 'g': case 'G': text = FormatGeneral(args.NextF64(), precision < 0 ? 6 : precision, conv == 'G', plus, space); break;
                case 'c': text = ((char)(byte)args.NextI64()).ToString(); break;
                case 's': { string s = ReadCString(mem, args.NextPtr()); text = precision >= 0 && precision < s.Length ? s[..precision] : s; break; }
                default: sb.Append('%').Append(conv); continue;
            }
            sb.Append(PadField(text, width, left, zero && conv is 'd' or 'i' or 'u' or 'x' or 'X' or 'o' or 'f' or 'F' or 'e' or 'E' or 'g' or 'G'));
        }
        return sb.ToString();
    }

    private static string FormatSigned(long v, bool plus, bool space, int precision)
    {
        bool negative = v < 0;
        string digits = Pad0((negative ? unchecked((ulong)(-v)) : (ulong)v).ToString(CultureInfo.InvariantCulture), precision);
        return (negative ? "-" : plus ? "+" : space ? " " : "") + digits;
    }

    private static string FormatFloat(double v, int precision, bool plus, bool space)
    {
        string body = Math.Abs(v).ToString("F" + precision, CultureInfo.InvariantCulture);
        return (v < 0 ? "-" : plus ? "+" : space ? " " : "") + body;
    }

    /// <summary>C <c>%e</c>/<c>%E</c>: mantissa with <paramref name="precision"/> fraction digits and a
    /// sign + at-least-two-digit exponent (C89 §7.9.6.1), vs .NET's three-digit exponent.</summary>
    private static string FormatScientific(double v, int precision, bool upper, bool plus, bool space)
    {
        string body = NormalizeExponent(Math.Abs(v).ToString("e" + precision, CultureInfo.InvariantCulture), upper);
        return (v < 0 ? "-" : plus ? "+" : space ? " " : "") + body;
    }

    /// <summary>C <c>%g</c>/<c>%G</c>: pick <c>%e</c> or <c>%f</c> by exponent and strip trailing zeros
    /// (no <c>#</c> flag), with precision counting significant digits (0 treated as 1).</summary>
    private static string FormatGeneral(double v, int precision, bool upper, bool plus, bool space)
    {
        int p = precision == 0 ? 1 : precision;
        double av = Math.Abs(v);
        string sci = av.ToString("e" + (p - 1), CultureInfo.InvariantCulture); // p significant digits
        int eIdx = sci.IndexOf('e');
        int exp = int.Parse(sci[(eIdx + 1)..], CultureInfo.InvariantCulture);
        string sign = v < 0 ? "-" : plus ? "+" : space ? " " : "";

        string body = exp >= -4 && exp < p
            ? StripTrailingZeros(av.ToString("F" + Math.Max(0, p - 1 - exp), CultureInfo.InvariantCulture))
            : StripTrailingZeros(sci[..eIdx]) + (upper ? 'E' : 'e') + ExponentTail(exp);
        return sign + body;
    }

    /// <summary>Rewrites a .NET scientific string's exponent to C's sign + minimum-two-digit form.</summary>
    private static string NormalizeExponent(string s, bool upper)
    {
        int e = s.IndexOf('e');
        if (e < 0) return s;
        return s[..e] + (upper ? 'E' : 'e') + ExponentTail(int.Parse(s[(e + 1)..], CultureInfo.InvariantCulture));
    }

    private static string ExponentTail(int exp)
    {
        string digits = Math.Abs(exp).ToString(CultureInfo.InvariantCulture);
        return (exp < 0 ? "-" : "+") + (digits.Length < 2 ? digits.PadLeft(2, '0') : digits);
    }

    private static string StripTrailingZeros(string s)
    {
        if (s.IndexOf('.') < 0) return s;
        s = s.TrimEnd('0');
        return s.EndsWith('.') ? s[..^1] : s;
    }

    private static string Pad0(string digits, int precision) => precision >= 0 ? digits.PadLeft(precision, '0') : digits;

    private static string ToOctal(ulong v)
    {
        if (v == 0) return "0";
        var sb = new StringBuilder();
        while (v > 0) { sb.Insert(0, (char)('0' + (int)(v & 7))); v >>= 3; }
        return sb.ToString();
    }

    private static string PadField(string s, int width, bool left, bool zero)
    {
        if (s.Length >= width) return s;
        if (left) return s.PadRight(width);
        if (zero && s.Length > 0 && s[0] is '-' or '+' or ' ')
            return s[0] + s[1..].PadLeft(width - 1, '0');
        return s.PadLeft(width, zero ? '0' : ' ');
    }

    // --- <stdio.h> input --------------------------------------------------

    [CExport("getchar")]
    public static int Getchar(CMemory mem) => Handle(mem, new CPtr(3)).Read(); // stdin; -1 at EOF

    [CExport("fgetc")]
    public static int Fgetc(CMemory mem, CPtr stream) => Handle(mem, stream).Read();

    [CExport("getc")] public static int Getc(CMemory mem, CPtr stream) => Handle(mem, stream).Read();
    [CExport("putc")] public static int Putc(CMemory mem, int c, CPtr stream) => Fputc(mem, c, stream);

    [CExport("feof")] public static int Feof(CMemory mem, CPtr stream) => Handle(mem, stream).Eof ? 1 : 0;
    [CExport("ferror")] public static int Ferror(CMemory mem, CPtr stream) => Handle(mem, stream).Error ? 1 : 0;

    [CExport("clearerr")]
    public static void Clearerr(CMemory mem, CPtr stream)
    {
        var f = Handle(mem, stream);
        f.Eof = false;
        f.Error = false;
    }

    [CExport("ungetc")]
    public static int Ungetc(CMemory mem, int c, CPtr stream)
        => c < 0 ? -1 : Handle(mem, stream).Ungetc(c) ? c & 0xFF : -1;

    [CExport("remove")]
    public static int Remove(CMemory mem, CPtr path)
    {
        string file = ReadCString(mem, path);
        try
        {
            // C requires a nonzero result when the file does not exist; .NET's Delete is silent there.
            if (File.Exists(file)) { File.Delete(file); return 0; }
            if (Directory.Exists(file)) { Directory.Delete(file); return 0; }
            return -1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return -1; }
    }

    [CExport("rename")]
    public static int Rename(CMemory mem, CPtr from, CPtr to)
    {
        try { File.Move(ReadCString(mem, from), ReadCString(mem, to), overwrite: true); return 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return -1; }
    }

    [CExport("fgets")]
    public static CPtr Fgets(CMemory mem, CPtr buffer, int size, CPtr stream)
    {
        if (size <= 0) return CPtr.Null;
        var input = Handle(mem, stream);
        int written = 0;
        while (written < size - 1)
        {
            int ch = input.Read();
            if (ch < 0) break;
            mem.WriteUInt8(buffer + written, (byte)ch);
            written++;
            if (ch == '\n') break;
        }
        if (written == 0) return CPtr.Null; // nothing read (EOF)
        mem.WriteUInt8(buffer + written, 0);
        return buffer;
    }

    [CExport("scanf")]
    public static int Scanf(CMemory mem, CPtr format, params object[] args) => RunScanf(mem, Handle(mem, new CPtr(3)), format, args);

    [CExport("fscanf")]
    public static int Fscanf(CMemory mem, CPtr stream, CPtr format, params object[] args) => RunScanf(mem, Handle(mem, stream), format, args);

    [CExport("sscanf")]
    public static int Sscanf(CMemory mem, CPtr s, CPtr format, params object[] args)
    {
        string input = ReadCString(mem, s);
        var bytes = new byte[input.Length];
        for (int i = 0; i < input.Length; i++) bytes[i] = (byte)input[i];
        return RunScanf(mem, new CFile { Stream = new MemoryStream(bytes) }, format, args); // read from the string
    }

    /// <summary>
    /// A small <c>scanf</c>: supports <c>%d %u %x %f (and %lf) %c %s %%</c> with an optional field
    /// width and assignment suppression (<c>*</c>). Arguments are pointers to the storage to fill;
    /// returns the number of items assigned (EOF if the stream ends before the first assignment).
    /// </summary>
    private static int RunScanf(CMemory mem, CFile input, CPtr format, object[] args)
    {
        string fmt = ReadCString(mem, format);
        int assigned = 0, argIndex = 0;

        for (int i = 0; i < fmt.Length; i++)
        {
            char f = fmt[i];
            if (char.IsWhiteSpace(f)) { SkipSpace(input); continue; }
            if (f != '%')
            {
                if (input.Peek() == f) input.Read(); else break; // match a literal
                continue;
            }

            i++;
            if (i >= fmt.Length) break;
            bool suppress = fmt[i] == '*';
            if (suppress) i++;

            int width = 0;
            while (i < fmt.Length && char.IsAsciiDigit(fmt[i])) width = width * 10 + (fmt[i++] - '0');

            bool isLong = false;
            while (i < fmt.Length && "lhL".IndexOf(fmt[i]) >= 0) { if (fmt[i] == 'l') isLong = true; i++; }
            if (i >= fmt.Length) break;

            char conv = fmt[i];
            CPtr Target() => (CPtr)args[argIndex++];
            switch (conv)
            {
                case '%':
                    if (input.Peek() == '%') input.Read(); else return assigned;
                    break;
                case 'd': case 'u':
                    SkipSpace(input);
                    if (!TryReadInteger(input, width, 10, out long dv)) return assigned == 0 ? -1 : assigned;
                    if (!suppress) { mem.WriteInt32(Target(), unchecked((int)dv)); assigned++; }
                    break;
                case 'x':
                    SkipSpace(input);
                    if (!TryReadInteger(input, width, 16, out long xv)) return assigned == 0 ? -1 : assigned;
                    if (!suppress) { mem.WriteInt32(Target(), unchecked((int)xv)); assigned++; }
                    break;
                case 'f': case 'e': case 'g':
                    SkipSpace(input);
                    if (!TryReadDouble(input, width, out double fv)) return assigned == 0 ? -1 : assigned;
                    if (!suppress) { if (isLong) mem.WriteDouble(Target(), fv); else mem.WriteSingle(Target(), (float)fv); assigned++; }
                    break;
                case 'c':
                {
                    int ch = input.Read();
                    if (ch < 0) return assigned == 0 ? -1 : assigned;
                    if (!suppress) { mem.WriteUInt8(Target(), (byte)ch); assigned++; }
                    break;
                }
                case 's':
                {
                    SkipSpace(input);
                    CPtr target = suppress ? CPtr.Null : Target();
                    int n = 0, p;
                    while ((p = input.Peek()) >= 0 && !char.IsWhiteSpace((char)p) && (width == 0 || n < width))
                    {
                        if (!suppress) mem.WriteUInt8(target + n, (byte)input.Read()); else input.Read();
                        n++;
                    }
                    if (n == 0) return assigned == 0 ? -1 : assigned;
                    if (!suppress) { mem.WriteUInt8(target + n, 0); assigned++; }
                    break;
                }
            }
        }
        return assigned;
    }

    private static void SkipSpace(CFile input)
    {
        int c;
        while ((c = input.Peek()) >= 0 && char.IsWhiteSpace((char)c)) input.Read();
    }

    private static bool TryReadInteger(CFile input, int maxWidth, int radix, out long value)
    {
        value = 0;
        int read = 0, sign = 1, c = input.Peek();
        if (radix == 10 && (c == '+' || c == '-')) { if (c == '-') sign = -1; input.Read(); }
        bool any = false;
        while ((maxWidth == 0 || read < maxWidth) && (c = input.Peek()) >= 0)
        {
            int digit = DigitValue((char)c, radix);
            if (digit < 0) break;
            value = value * radix + digit;
            input.Read();
            read++;
            any = true;
        }
        value *= sign;
        return any;
    }

    private static int DigitValue(char c, int radix)
    {
        int v = c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };
        return v >= 0 && v < radix ? v : -1;
    }

    private static bool TryReadDouble(CFile input, int maxWidth, out double value)
    {
        var sb = new StringBuilder();
        int read = 0, c;
        bool Take() { sb.Append((char)input.Read()); read++; return true; }
        bool Room() => maxWidth == 0 || read < maxWidth;

        if (Room() && ((c = input.Peek()) == '+' || c == '-')) Take();
        while (Room() && (c = input.Peek()) >= '0' && c <= '9') Take();
        if (Room() && input.Peek() == '.') Take();
        while (Room() && (c = input.Peek()) >= '0' && c <= '9') Take();
        if (Room() && ((c = input.Peek()) == 'e' || c == 'E'))
        {
            Take();
            if (Room() && ((c = input.Peek()) == '+' || c == '-')) Take();
            while (Room() && (c = input.Peek()) >= '0' && c <= '9') Take();
        }
        return double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string ReadCString(CMemory mem, CPtr s)
    {
        var sb = new StringBuilder();
        for (long i = 0; ; i++)
        {
            byte c = mem.ReadUInt8(s + i);
            if (c == 0) break;
            sb.Append((char)c);
        }
        return sb.ToString();
    }
}
