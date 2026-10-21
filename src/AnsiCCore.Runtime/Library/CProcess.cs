using System.Runtime.CompilerServices;
using AnsiCCore.Runtime.Memory;

namespace AnsiCCore.Runtime.Library;

/// <summary>
/// Thrown by <c>exit</c> to unwind the program to its entry point, carrying the status code. The
/// generated <c>Main</c> catches it and returns <see cref="Code"/> — the managed analog of process
/// termination (C89 §7.10.4.3).
/// </summary>
public sealed class CExitException : Exception
{
    public CExitException(int code) => Code = code;
    public int Code { get; }
}

/// <summary>Thrown by <c>abort</c> for abnormal termination; the generated <c>Main</c> maps it to a
/// conventional failure status and does not run <c>atexit</c> handlers (C89 §7.10.4.1).</summary>
public sealed class CAbortException : Exception;

/// <summary>
/// <c>&lt;stdlib.h&gt;</c> process-control shims: <c>exit</c>, <c>abort</c>, and <c>atexit</c>.
/// Registered exit handlers live per-program (keyed by <see cref="CMemory"/>, like the stream
/// table) and run in last-registered-first order on <c>exit</c> or a normal return from <c>main</c>.
/// </summary>
public static class CProcess
{
    private static readonly ConditionalWeakTable<CMemory, List<Action>> Handlers = new();
    private static List<Action> List(CMemory mem) => Handlers.GetValue(mem, _ => new List<Action>());

    /// <summary><c>atexit</c>: registers a handler to run at program exit; returns 0 on success.</summary>
    [CExport("atexit")]
    public static int AtExit(CMemory mem, Action func)
    {
        List(mem).Add(func);
        return 0;
    }

    /// <summary><c>exit</c>: runs the registered handlers, then unwinds to the entry point with the status.</summary>
    [CExport("exit")]
    public static void Exit(CMemory mem, int status)
    {
        RunAtExit(mem);
        throw new CExitException(status);
    }

    /// <summary><c>abort</c>: abnormal termination with no handler processing.</summary>
    [CExport("abort")]
    public static void Abort() => throw new CAbortException();

    /// <summary>
    /// Runs and clears the registered <c>atexit</c> handlers in reverse order. Called by <c>exit</c>
    /// and by the generated entry point when <c>main</c> returns normally (C89 §5.1.2.2.3 makes the
    /// two equivalent).
    /// </summary>
    public static void RunAtExit(CMemory mem)
    {
        if (!Handlers.TryGetValue(mem, out var list)) return;
        for (int i = list.Count - 1; i >= 0; i--)
            list[i]();
        list.Clear();
    }
}
