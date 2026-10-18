namespace AnsiCCore.Runtime.Library;

/// <summary>
/// Marks a public static method as the implementation of a C function, so the transpiler can
/// "link" a call to a declared-but-undefined C function (an <c>extern</c> prototype) directly to
/// precompiled .NET code — the managed analog of resolving a symbol against a link-time object.
/// </summary>
/// <remarks>
/// Calling convention the emitter expects:
/// <list type="bullet">
/// <item>An optional first parameter of type <see cref="Memory.CMemory"/> receives the program's
/// address space (for shims that touch memory); the emitter passes it automatically.</item>
/// <item>The remaining fixed parameters correspond, in order, to the C arguments; the emitter
/// converts each C argument to the parameter's CLR type.</item>
/// <item>A trailing <c>params object[]</c> parameter receives the variadic C arguments, boxed.</item>
/// </list>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CExportAttribute : Attribute
{
    public CExportAttribute(string name) => Name = name;

    /// <summary>The C function name this method implements.</summary>
    public string Name { get; }
}
