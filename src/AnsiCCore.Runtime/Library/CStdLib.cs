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

    [CExport("strcpy")]
    public static CPtr Strcpy(CMemory mem, CPtr dst, CPtr src)
    {
        long i = 0;
        byte c;
        do { c = mem.ReadUInt8(src + i); mem.WriteUInt8(dst + i, c); i++; } while (c != 0);
        return dst;
    }

    // --- <stdio.h> (output subset) ---------------------------------------

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

    /// <summary>
    /// A small <c>printf</c>: supports the <c>%d %i %u %x %c %s %f %%</c> conversions. Flags, field
    /// width, and precision are skipped (not applied) and length modifiers (<c>h l L z</c>) are
    /// tolerated. Arguments arrive boxed in declaration order.
    /// </summary>
    [CExport("printf")]
    public static int Printf(CMemory mem, CPtr format, params object[] args)
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

        string output = sb.ToString();
        Console.Out.Write(output);
        return output.Length;
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
