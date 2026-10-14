using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Semantics.Symbols;

/// <summary>Which lexical level a scope represents (affects some redeclaration rules).</summary>
public enum ScopeKind
{
    File,
    FunctionPrototype,
    Block,
}

/// <summary>
/// A lexical scope holding the two name-lookup namespaces C uses here: <em>ordinary</em>
/// identifiers (objects, functions, typedefs, enum constants) and <em>tags</em> (struct/union/enum
/// names). Scopes nest via <see cref="Parent"/>; lookup walks outward, declaration is local.
/// (Labels are a per-function namespace handled by the analyzer; members live on the record type.)
/// </summary>
public sealed class Scope
{
    private readonly Dictionary<string, Symbol> _ordinary = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CType> _tags = new(StringComparer.Ordinal);

    public Scope(ScopeKind kind, Scope? parent)
    {
        Kind = kind;
        Parent = parent;
    }

    public ScopeKind Kind { get; }
    public Scope? Parent { get; }
    public bool IsFileScope => Kind == ScopeKind.File;

    public IReadOnlyDictionary<string, Symbol> OrdinarySymbols => _ordinary;
    public IReadOnlyDictionary<string, CType> Tags => _tags;

    // --- ordinary namespace ----------------------------------------------

    public Symbol? LookupOrdinary(string name)
    {
        for (Scope? s = this; s != null; s = s.Parent)
            if (s._ordinary.TryGetValue(name, out var sym))
                return sym;
        return null;
    }

    public Symbol? LookupOrdinaryLocal(string name) => _ordinary.GetValueOrDefault(name);

    public void DeclareOrdinary(Symbol symbol) => _ordinary[symbol.Name] = symbol;

    // --- tag namespace ----------------------------------------------------

    public CType? LookupTag(string name)
    {
        for (Scope? s = this; s != null; s = s.Parent)
            if (s._tags.TryGetValue(name, out var tag))
                return tag;
        return null;
    }

    public CType? LookupTagLocal(string name) => _tags.GetValueOrDefault(name);

    public void DeclareTag(string name, CType type) => _tags[name] = type;
}
