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

    // --- <stdio.h> streams ------------------------------------------------
    //
    // FILE* is a sentinel pointer. The stub stdio.h defines stdin/stdout/stderr as
    // ((FILE*)3/1/2); fopen allocates further sentinels. A per-CMemory registry maps each
    // sentinel to a CFile, so standard streams honor Console redirection and files are real.

    private sealed class CFile
    {
        public enum Std { None, In, Out, Err }
        public Std Standard;
        public TextReader? Reader;
        public TextWriter? Writer;

        public TextWriter? Out => Standard switch { Std.Out => Console.Out, Std.Err => Console.Error, _ => Writer };
        public TextReader? In => Standard == Std.In ? Console.In : Reader;
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

    private static TextWriter Writer(CMemory mem, CPtr stream) => Table(mem).Files.GetValueOrDefault(stream.Address)?.Out ?? Console.Out;
    private static TextReader Reader(CMemory mem, CPtr stream) => Table(mem).Files.GetValueOrDefault(stream.Address)?.In ?? Console.In;

    [CExport("fopen")]
    public static CPtr Fopen(CMemory mem, CPtr path, CPtr mode)
    {
        string file = ReadCString(mem, path), m = ReadCString(mem, mode);
        try
        {
            CFile handle = m.Length > 0 && m[0] switch { 'r' => true, _ => false }
                ? new CFile { Reader = new StreamReader(file) }
                : m.Length > 0 && m[0] == 'a'
                    ? new CFile { Writer = new StreamWriter(file, append: true) }
                    : m.Length > 0 && m[0] == 'w'
                        ? new CFile { Writer = new StreamWriter(file, append: false) }
                        : throw new IOException($"unsupported fopen mode '{m}'");

            var table = Table(mem);
            ulong address = table.Next++;
            table.Files[address] = handle;
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
        handle.Writer?.Flush();
        handle.Writer?.Dispose();
        handle.Reader?.Dispose();
        table.Files.Remove(stream.Address);
        return 0;
    }

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
        Writer(mem, stream).Write((char)(byte)c);
        return c & 0xFF;
    }

    [CExport("fputs")]
    public static int Fputs(CMemory mem, CPtr s, CPtr stream)
    {
        Writer(mem, stream).Write(ReadCString(mem, s));
        return 0;
    }

    [CExport("printf")]
    public static int Printf(CMemory mem, CPtr format, params object[] args)
    {
        string output = Format(mem, format, args);
        Console.Out.Write(output);
        return output.Length;
    }

    [CExport("fprintf")]
    public static int Fprintf(CMemory mem, CPtr stream, CPtr format, params object[] args)
    {
        string output = Format(mem, format, args);
        Writer(mem, stream).Write(output);
        return output.Length;
    }

    [CExport("sprintf")]
    public static int Sprintf(CMemory mem, CPtr buffer, CPtr format, params object[] args)
    {
        string output = Format(mem, format, args);
        for (int i = 0; i < output.Length; i++) mem.WriteUInt8(buffer + i, (byte)output[i]);
        mem.WriteUInt8(buffer + output.Length, 0);
        return output.Length;
    }

    /// <summary>
    /// The shared <c>printf</c>-family formatter: supports <c>%d %i %u %x %c %s %f %%</c> with the
    /// <c>- + space 0</c> flags, field width, and precision (and tolerated length modifiers).
    /// Arguments arrive boxed in declaration order.
    /// </summary>
    private static string Format(CMemory mem, CPtr format, object[] args)
    {
        var inv = CultureInfo.InvariantCulture;
        string fmt = ReadCString(mem, format);
        var sb = new StringBuilder();
        int argIndex = 0, i = 0;

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
            while (i < fmt.Length && char.IsAsciiDigit(fmt[i])) width = width * 10 + (fmt[i++] - '0');

            int precision = -1;
            if (i < fmt.Length && fmt[i] == '.')
            {
                i++;
                precision = 0;
                while (i < fmt.Length && char.IsAsciiDigit(fmt[i])) precision = precision * 10 + (fmt[i++] - '0');
            }

            while (i < fmt.Length && "lhLzjt".IndexOf(fmt[i]) >= 0) i++;
            if (i >= fmt.Length) break;
            char conv = fmt[i++];

            object Next() => args[argIndex++];
            string text;
            switch (conv)
            {
                case '%': sb.Append('%'); continue;
                case 'd': case 'i': text = FormatSigned(Convert.ToInt64(Next(), inv), plus, space, precision); break;
                case 'u': text = Pad0(Convert.ToUInt64(Next(), inv).ToString(inv), precision); break;
                case 'x': text = Pad0(Convert.ToUInt64(Next(), inv).ToString("x", inv), precision); break;
                case 'f': case 'F': text = FormatFloat(Convert.ToDouble(Next(), inv), precision < 0 ? 6 : precision, plus, space); break;
                case 'c': text = ((char)(byte)Convert.ToInt32(Next(), inv)).ToString(); break;
                case 's': { string s = ReadCString(mem, (CPtr)Next()); text = precision >= 0 && precision < s.Length ? s[..precision] : s; break; }
                default: sb.Append('%').Append(conv); continue;
            }
            sb.Append(PadField(text, width, left, zero && conv is 'd' or 'i' or 'u' or 'x' or 'f' or 'F'));
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

    private static string Pad0(string digits, int precision) => precision >= 0 ? digits.PadLeft(precision, '0') : digits;

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
    public static int Getchar(CMemory mem) => Console.In.Read(); // -1 at end of input == EOF

    [CExport("fgetc")]
    public static int Fgetc(CMemory mem, CPtr stream) => Reader(mem, stream).Read();

    [CExport("fgets")]
    public static CPtr Fgets(CMemory mem, CPtr buffer, int size, CPtr stream)
    {
        if (size <= 0) return CPtr.Null;
        var input = Reader(mem, stream);
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
    public static int Scanf(CMemory mem, CPtr format, params object[] args) => RunScanf(mem, Console.In, format, args);

    [CExport("fscanf")]
    public static int Fscanf(CMemory mem, CPtr stream, CPtr format, params object[] args) => RunScanf(mem, Reader(mem, stream), format, args);

    /// <summary>
    /// A small <c>scanf</c>: supports <c>%d %u %x %f (and %lf) %c %s %%</c> with an optional field
    /// width and assignment suppression (<c>*</c>). Arguments are pointers to the storage to fill;
    /// returns the number of items assigned (EOF if the stream ends before the first assignment).
    /// </summary>
    private static int RunScanf(CMemory mem, TextReader input, CPtr format, object[] args)
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

    private static void SkipSpace(TextReader input)
    {
        int c;
        while ((c = input.Peek()) >= 0 && char.IsWhiteSpace((char)c)) input.Read();
    }

    private static bool TryReadInteger(TextReader input, int maxWidth, int radix, out long value)
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

    private static bool TryReadDouble(TextReader input, int maxWidth, out double value)
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
