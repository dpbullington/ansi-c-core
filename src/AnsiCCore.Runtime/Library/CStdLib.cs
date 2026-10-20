using System.Globalization;
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

    // --- <stdio.h> output -------------------------------------------------
    //
    // FILE* streams are modeled as sentinel pointers (see the stdout/stderr/stdin macros in the
    // stub stdio.h): address 1 = stdout, 2 = stderr; anything else falls back to stdout.

    private static TextWriter Writer(CPtr stream) => stream.Address == 2 ? Console.Error : Console.Out;

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
        Writer(stream).Write((char)(byte)c);
        return c & 0xFF;
    }

    [CExport("fputs")]
    public static int Fputs(CMemory mem, CPtr s, CPtr stream)
    {
        Writer(stream).Write(ReadCString(mem, s));
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
        Writer(stream).Write(output);
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
    /// The shared <c>printf</c>-family formatter: supports the <c>%d %i %u %x %c %s %f %%</c>
    /// conversions. Flags, field width, and precision are skipped (not applied); length modifiers
    /// (<c>h l L z</c>) are tolerated. Arguments arrive boxed in declaration order.
    /// </summary>
    private static string Format(CMemory mem, CPtr format, object[] args)
    {
        string fmt = ReadCString(mem, format);
        var sb = new StringBuilder();
        int argIndex = 0;

        for (int i = 0; i < fmt.Length; i++)
        {
            if (fmt[i] != '%') { sb.Append(fmt[i]); continue; }
            i++;
            while (i < fmt.Length && "-+ #0123456789.lhLjztZ".IndexOf(fmt[i]) >= 0) i++; // skip flags/width/precision/length
            if (i >= fmt.Length) break;

            object Next() => args[argIndex++];
            switch (fmt[i])
            {
                case '%': sb.Append('%'); break;
                case 'd': case 'i': sb.Append(Convert.ToInt64(Next(), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); break;
                case 'u': sb.Append(Convert.ToUInt64(Next(), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); break;
                case 'x': sb.Append(Convert.ToInt64(Next(), CultureInfo.InvariantCulture).ToString("x", CultureInfo.InvariantCulture)); break;
                case 'f': case 'F': sb.Append(Convert.ToDouble(Next(), CultureInfo.InvariantCulture).ToString("F6", CultureInfo.InvariantCulture)); break;
                case 'c': sb.Append((char)(byte)Convert.ToInt32(Next(), CultureInfo.InvariantCulture)); break;
                case 's': sb.Append(ReadCString(mem, (CPtr)Next())); break;
                default: sb.Append('%').Append(fmt[i]); break;
            }
        }
        return sb.ToString();
    }

    // --- <stdio.h> input (reads Console.In) -------------------------------

    [CExport("getchar")]
    public static int Getchar(CMemory mem) => Console.In.Read(); // -1 at end of input == EOF

    [CExport("fgets")]
    public static CPtr Fgets(CMemory mem, CPtr buffer, int size, CPtr stream)
    {
        if (size <= 0) return CPtr.Null;
        int written = 0;
        while (written < size - 1)
        {
            int ch = Console.In.Read();
            if (ch < 0) break;
            mem.WriteUInt8(buffer + written, (byte)ch);
            written++;
            if (ch == '\n') break;
        }
        if (written == 0) return CPtr.Null; // nothing read (EOF)
        mem.WriteUInt8(buffer + written, 0);
        return buffer;
    }

    /// <summary>
    /// A small <c>scanf</c>: supports <c>%d %u %c %s %%</c>, with whitespace in the format matching
    /// optional input whitespace. Arguments are pointers to the storage to fill. Returns the number
    /// of items assigned (EOF if the stream ends before the first assignment).
    /// </summary>
    [CExport("scanf")]
    public static int Scanf(CMemory mem, CPtr format, params object[] args)
    {
        string fmt = ReadCString(mem, format);
        var input = Console.In;
        int assigned = 0, argIndex = 0;

        for (int i = 0; i < fmt.Length; i++)
        {
            char f = fmt[i];
            if (char.IsWhiteSpace(f)) { SkipSpace(input); continue; }
            if (f != '%')
            {
                if (input.Peek() == f) input.Read(); // match a literal
                else break;
                continue;
            }

            i++;
            if (i >= fmt.Length) break;
            switch (fmt[i])
            {
                case '%':
                    if (input.Peek() == '%') input.Read(); else return assigned;
                    break;
                case 'd': case 'u':
                {
                    SkipSpace(input);
                    if (!TryReadLong(input, out long value)) return assigned == 0 ? -1 : assigned;
                    mem.WriteInt32((CPtr)args[argIndex++], unchecked((int)value));
                    assigned++;
                    break;
                }
                case 'c':
                {
                    int ch = input.Read();
                    if (ch < 0) return assigned == 0 ? -1 : assigned;
                    mem.WriteUInt8((CPtr)args[argIndex++], (byte)ch);
                    assigned++;
                    break;
                }
                case 's':
                {
                    SkipSpace(input);
                    var target = (CPtr)args[argIndex++];
                    int n = 0, c;
                    while ((c = input.Peek()) >= 0 && !char.IsWhiteSpace((char)c))
                        mem.WriteUInt8(target + n++, (byte)input.Read());
                    if (n == 0) return assigned == 0 ? -1 : assigned;
                    mem.WriteUInt8(target + n, 0);
                    assigned++;
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

    private static bool TryReadLong(TextReader input, out long value)
    {
        value = 0;
        int sign = 1, c = input.Peek();
        if (c == '+' || c == '-') { if (c == '-') sign = -1; input.Read(); }
        bool any = false;
        while ((c = input.Peek()) >= '0' && c <= '9') { value = value * 10 + (input.Read() - '0'); any = true; }
        value *= sign;
        return any;
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
