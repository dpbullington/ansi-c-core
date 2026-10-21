using System.Globalization;
using AnsiCCore.Runtime.Memory;

namespace AnsiCCore.Runtime.Library;

/// <summary>
/// Runtime support for <c>&lt;stdarg.h&gt;</c> (variadic user functions). A variadic C function
/// receives a hidden <c>va_list</c> base pointer to a region holding its extra arguments, one
/// 8-byte little-endian slot per argument, after the default argument promotions (so every
/// promoted scalar — <c>int</c>, <c>unsigned</c>, <c>long</c>, <c>double</c>, a pointer — fits a
/// slot). <see cref="Pack"/> builds that region at the call site; <see cref="NextArg"/> (the
/// <c>__va_arg</c> intrinsic) hands back the slot the list currently points at and advances it,
/// and the generated <c>*(T *)…</c> around the call performs the typed read of that slot.
/// </summary>
/// <remarks>
/// Storing each value in a full 8-byte slot (integers widened to 64 bits) means a later
/// <c>va_arg(ap, T)</c> that reads the low bytes of the slot recovers the value for any T matching
/// the promoted argument type — the little-endian layout makes a 4-byte read of a widened int
/// correct. This mirrors how a real C variadic frame walks promoted arguments on the stack.
/// </remarks>
public static class CVarArgs
{
    /// <summary>One machine word per promoted argument; all promoted scalars fit in 8 bytes.</summary>
    public const int SlotSize = 8;

    /// <summary>
    /// Packs promoted variadic arguments into a fresh automatic region and returns its base (the
    /// hidden <c>va_list</c> the callee receives). With no variadic arguments this is a zero-length
    /// region that <c>va_arg</c> is never meant to read from.
    /// </summary>
    public static CPtr Pack(CMemory mem, params object[] args)
    {
        var basePtr = mem.Allocate((long)args.Length * SlotSize, RegionKind.Automatic);
        for (int i = 0; i < args.Length; i++)
        {
            var slot = basePtr + (long)i * SlotSize;
            switch (args[i])
            {
                case double d: mem.WriteDouble(slot, d); break;
                case float f: mem.WriteDouble(slot, f); break; // defensive: floats promote to double
                case CPtr p: mem.WritePointer(slot, p); break;
                case ulong u: mem.WriteInt64(slot, unchecked((long)u)); break;
                case long l: mem.WriteInt64(slot, l); break;
                case uint u: mem.WriteInt64(slot, u); break;
                case int n: mem.WriteInt64(slot, n); break;
                case null: mem.WriteInt64(slot, 0); break;
                default: mem.WriteInt64(slot, Convert.ToInt64(args[i], CultureInfo.InvariantCulture)); break;
            }
        }
        return basePtr;
    }

    /// <summary>
    /// The <c>__va_arg</c> intrinsic behind the <c>va_arg</c> macro: given <paramref name="pap"/> =
    /// the address of the caller's <c>va_list</c>, returns the slot it currently points at and
    /// advances the list by one slot. The emitted <c>*(T *)…</c> cast around this call reads the
    /// slot as the requested type.
    /// </summary>
    [CExport("__va_arg")]
    public static CPtr NextArg(CMemory mem, CPtr pap)
    {
        var cursor = mem.ReadPointer(pap);
        mem.WritePointer(pap, cursor + SlotSize);
        return cursor;
    }
}
