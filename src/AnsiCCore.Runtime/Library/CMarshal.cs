using System.Text;
using AnsiCCore.Runtime.Memory;

namespace AnsiCCore.Runtime.Library;

/// <summary>
/// Conversions the emitter uses to marshal richer CLR types across the C boundary for
/// <c>[CExport]</c> methods — currently C strings (<c>char *</c>) to and from managed
/// <see cref="string"/>. C text is byte-oriented, so each byte maps to one char (Latin-1).
/// </summary>
public static class CMarshal
{
    /// <summary>Reads a NUL-terminated C string from memory into a managed string.</summary>
    public static string ToManaged(CMemory mem, CPtr s)
    {
        if (s.IsNull) return string.Empty;
        var sb = new StringBuilder();
        for (long i = 0; ; i++)
        {
            byte c = mem.ReadUInt8(s + i);
            if (c == 0) break;
            sb.Append((char)c);
        }
        return sb.ToString();
    }

    /// <summary>Writes a managed string into a fresh NUL-terminated region and returns a pointer to it.</summary>
    public static CPtr FromManaged(CMemory mem, string s)
    {
        var bytes = new byte[s.Length + 1];
        for (int i = 0; i < s.Length; i++) bytes[i] = (byte)s[i];
        var ptr = mem.Allocate(bytes.Length, RegionKind.Automatic);
        for (int i = 0; i < bytes.Length; i++) mem.WriteUInt8(ptr + i, bytes[i]);
        return ptr;
    }

    /// <summary>
    /// Builds a C <c>argv</c> vector for <c>main(int argc, char **argv)</c>: each managed string is
    /// copied into the address space, their pointers are stored in a contiguous array, and a
    /// terminating null pointer is appended (C89 §5.1.2.2.1 guarantees <c>argv[argc] == NULL</c>).
    /// Returns the vector base; <paramref name="args"/> already includes <c>argv[0]</c>.
    /// </summary>
    public static CPtr BuildArgv(CMemory mem, string[] args)
    {
        var vector = mem.Allocate((long)(args.Length + 1) * 8, RegionKind.Static);
        for (int i = 0; i < args.Length; i++)
            mem.WritePointer(vector + (long)i * 8, FromManaged(mem, args[i]));
        mem.WritePointer(vector + (long)args.Length * 8, CPtr.Null);
        return vector;
    }
}
