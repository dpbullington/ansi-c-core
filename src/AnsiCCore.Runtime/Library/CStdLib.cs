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

    public static CPtr Malloc(CMemory mem, long size) => mem.Malloc(size);
    public static CPtr Calloc(CMemory mem, long count, long size) => mem.Calloc(count, size);
    public static CPtr Realloc(CMemory mem, CPtr ptr, long size) => mem.Realloc(ptr, size);
    public static void Free(CMemory mem, CPtr ptr) => mem.Free(ptr);

    // --- <string.h> -------------------------------------------------------

    public static CPtr Memcpy(CMemory mem, CPtr dst, CPtr src, long n) { mem.Copy(dst, src, n); return dst; }
    public static CPtr Memmove(CMemory mem, CPtr dst, CPtr src, long n) { mem.Copy(dst, src, n); return dst; }
    public static CPtr Memset(CMemory mem, CPtr dst, int value, long n) { mem.Set(dst, unchecked((byte)value), n); return dst; }
    public static int Memcmp(CMemory mem, CPtr a, CPtr b, long n) => Math.Sign(mem.Compare(a, b, n));

    public static int Strlen(CMemory mem, CPtr s)
    {
        int n = 0;
        while (mem.ReadUInt8(s + n) != 0) n++;
        return n;
    }

    public static int Strcmp(CMemory mem, CPtr a, CPtr b)
    {
        for (long i = 0; ; i++)
        {
            byte ca = mem.ReadUInt8(a + i), cb = mem.ReadUInt8(b + i);
            if (ca != cb) return ca < cb ? -1 : 1;
            if (ca == 0) return 0;
        }
    }

    public static CPtr Strcpy(CMemory mem, CPtr dst, CPtr src)
    {
        long i = 0;
        byte c;
        do { c = mem.ReadUInt8(src + i); mem.WriteUInt8(dst + i, c); i++; } while (c != 0);
        return dst;
    }

    // --- <stdio.h> (output subset) ---------------------------------------

    public static int Putchar(CMemory mem, int c)
    {
        Console.Out.Write((char)(byte)c);
        return c & 0xFF;
    }

    public static int Puts(CMemory mem, CPtr s)
    {
        Console.Out.Write(ReadCString(mem, s));
        Console.Out.Write('\n');
        return 0;
    }

    /// <summary>
    /// A small <c>printf</c>: supports <c>%d %i %u %c %s %x %% </c> and ignores width/precision.
    /// Arguments arrive boxed in declaration order.
    /// </summary>
    public static int Printf(CMemory mem, CPtr format, params object[] args)
    {
        string fmt = ReadCString(mem, format);
        var sb = new StringBuilder();
        int argIndex = 0;

        for (int i = 0; i < fmt.Length; i++)
        {
            if (fmt[i] != '%') { sb.Append(fmt[i]); continue; }
            i++;
            if (i >= fmt.Length) break;
            switch (fmt[i])
            {
                case '%': sb.Append('%'); break;
                case 'd': case 'i': sb.Append(Convert.ToInt64(args[argIndex++], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); break;
                case 'u': sb.Append(Convert.ToUInt64(args[argIndex++], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); break;
                case 'x': sb.Append(Convert.ToInt64(args[argIndex++], CultureInfo.InvariantCulture).ToString("x", CultureInfo.InvariantCulture)); break;
                case 'c': sb.Append((char)(byte)Convert.ToInt32(args[argIndex++], CultureInfo.InvariantCulture)); break;
                case 's': sb.Append(ReadCString(mem, (CPtr)args[argIndex++])); break;
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
