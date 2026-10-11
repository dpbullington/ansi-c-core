namespace AnsiCCore.Frontend.Preprocessing;

/// <summary>The set of currently-defined macros, keyed by name (case-sensitive).</summary>
public sealed class MacroTable
{
    private readonly Dictionary<string, Macro> _macros = new(StringComparer.Ordinal);

    public bool Contains(string name) => _macros.ContainsKey(name);

    public Macro? TryGet(string name) => _macros.GetValueOrDefault(name);

    /// <summary>Defines (or redefines) a macro. Returns the previous definition, if any.</summary>
    public Macro? Define(Macro macro)
    {
        _macros.TryGetValue(macro.Name, out var previous);
        _macros[macro.Name] = macro;
        return previous;
    }

    /// <summary>Removes a macro. Returns true if it existed.</summary>
    public bool Undef(string name) => _macros.Remove(name);
}
