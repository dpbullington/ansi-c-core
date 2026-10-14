using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Semantics.Symbols;

/// <summary>
/// The result of type-checking an expression: its C type and whether it is an lvalue
/// (an expression designating an object, which may appear on the left of an assignment or have
/// its address taken). Recorded per expression node for Emit to consume.
/// </summary>
public readonly record struct ExpressionType(QualType Type, bool IsLvalue);
