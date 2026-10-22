using System.Threading;
using AnsiCCore.Runtime.Memory;

namespace AnsiCCore.Runtime.Library;

/// <summary>
/// Carries a <c>longjmp</c> across the stack to the matching <c>setjmp</c>. The generated code wraps
/// an <c>if</c>/<c>switch</c> whose controlling expression is <c>setjmp(env)</c> in a loop+try/catch
/// that catches this and re-enters with <see cref="Value"/> as the setjmp result.
/// </summary>
public sealed class CLongjmp : Exception
{
    public CLongjmp(long token, int value) { Token = token; Value = value; }

    /// <summary>Identifies which setjmp activation this targets (the token stored in its jmp_buf).</summary>
    public long Token { get; }

    /// <summary>The value setjmp should appear to return (never 0).</summary>
    public int Value { get; }
}

/// <summary>
/// Runtime support for <c>&lt;setjmp.h&gt;</c>. A <c>jmp_buf</c> is a one-element <c>long</c> array
/// holding a token that uniquely identifies a live setjmp activation; <c>longjmp</c> reads it and
/// throws <see cref="CLongjmp"/>, which the emitted setjmp wrapper catches.
/// </summary>
public static class CSetjmp
{
    private static long _next;

    /// <summary>Arms <paramref name="env"/> with a fresh token (stored in the buffer) and returns it.
    /// Called by the generated setjmp wrapper on each entry to the guarded statement.</summary>
    public static long Arm(CMemory mem, CPtr env)
    {
        long token = Interlocked.Increment(ref _next);
        mem.WriteInt64(env, token);
        return token;
    }

    /// <summary><c>longjmp</c>: transfers control to the setjmp that armed <paramref name="env"/>,
    /// making it return <paramref name="value"/> (or 1 if <paramref name="value"/> is 0).</summary>
    [CExport("longjmp")]
    public static void Longjmp(CMemory mem, CPtr env, int value)
        => throw new CLongjmp(mem.ReadInt64(env), value == 0 ? 1 : value);
}
