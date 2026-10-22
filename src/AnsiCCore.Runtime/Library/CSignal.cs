using System.Runtime.CompilerServices;
using AnsiCCore.Runtime.Memory;

namespace AnsiCCore.Runtime.Library;

/// <summary>
/// <c>&lt;signal.h&gt;</c> shims. A transpiled program is synchronous, so there are no asynchronous
/// signals; this models the API faithfully for the usual pattern of registering a handler and
/// delivering it with <c>raise</c>. Handlers are managed delegates (C function pointers passed by
/// name), stored per-program and keyed by signal number. <c>SIG_IGN</c>/<c>SIG_ERR</c> are
/// reference-identity sentinel delegates; <c>SIG_DFL</c> is the null handler.
/// </summary>
public static class CSignal
{
    private static readonly Action<int> Ignore = _ => { };
    private static readonly Action<int> Error = _ => { };

    private static readonly ConditionalWeakTable<CMemory, Dictionary<int, Action<int>?>> Handlers = new();
    private static Dictionary<int, Action<int>?> Table(CMemory mem) => Handlers.GetValue(mem, _ => new Dictionary<int, Action<int>?>());

    [CExport("__sig_ign")] public static Action<int> SigIgn() => Ignore;
    [CExport("__sig_err")] public static Action<int> SigErr() => Error;

    /// <summary><c>signal</c>: installs a handler for <paramref name="sig"/>, returning the previous one.</summary>
    [CExport("signal")]
    public static Action<int>? Signal(CMemory mem, int sig, Action<int>? handler)
    {
        var table = Table(mem);
        table.TryGetValue(sig, out var previous);
        table[sig] = handler; // null == SIG_DFL
        return previous;
    }

    /// <summary><c>raise</c>: delivers <paramref name="sig"/> to its current handler synchronously.</summary>
    [CExport("raise")]
    public static int Raise(CMemory mem, int sig)
    {
        Table(mem).TryGetValue(sig, out var handler);
        if (handler is null || ReferenceEquals(handler, Ignore)) return 0; // default/ignored: no effect here
        if (ReferenceEquals(handler, Error)) return -1;
        Table(mem)[sig] = null; // reset to SIG_DFL before the call (C89 §7.7.1.1)
        handler(sig);
        return 0;
    }
}
