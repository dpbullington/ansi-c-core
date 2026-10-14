namespace AnsiCCore.Semantics.Types;

/// <summary>
/// Type qualifiers that may apply to any type. Kept separate from the type identity (see
/// <see cref="QualType"/>) so canonical type objects stay shareable.
/// </summary>
[Flags]
public enum TypeQualifiers
{
    None = 0,
    Const = 1,
    Volatile = 2,
}

/// <summary>
/// Base of the resolved C type model. <see cref="CType"/> objects are <em>unqualified</em> and
/// canonical: the basic types are singletons (see <see cref="CTypes"/>) and record/enum types
/// have reference identity, so two distinct <c>struct</c> definitions are never equal. Qualified
/// uses of a type are represented by wrapping it in a <see cref="QualType"/>.
/// </summary>
public abstract class CType
{
    /// <summary>A short, human-readable spelling for diagnostics and tests (e.g. <c>int *</c>).</summary>
    public abstract string Display { get; }

    /// <summary>Incomplete types (void, forward-declared tags, arrays of unknown bound) have no size.</summary>
    public virtual bool IsComplete => true;

    public override string ToString() => Display;
}

/// <summary>The <c>void</c> type — the canonical incomplete type that cannot be completed.</summary>
public sealed class VoidType : CType
{
    internal VoidType() { }
    public override string Display => "void";
    public override bool IsComplete => false;
}
