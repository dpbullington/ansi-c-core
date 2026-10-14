namespace AnsiCCore.Semantics.Types;

/// <summary>
/// The operations that make the type model a type <em>system</em>: size/alignment, the type
/// classification predicates, type compatibility, and the C89 conversion rules (integer
/// promotion and the usual arithmetic conversions).
/// </summary>
public static class TypeOperations
{
    // --- classification ---------------------------------------------------

    public static bool IsInteger(CType t) => t is IntegerType or EnumType;
    public static bool IsFloating(CType t) => t is FloatingType;
    public static bool IsArithmetic(CType t) => IsInteger(t) || IsFloating(t);
    public static bool IsScalar(CType t) => IsArithmetic(t) || t is PointerType;
    public static bool IsAggregate(CType t) => t is ArrayType || (t is RecordType { IsUnion: false });

    // --- size / alignment -------------------------------------------------

    /// <summary>Size in bytes, or null for an incomplete or sizeless type (void, function, incomplete tag).</summary>
    public static long? SizeOf(CType t) => t switch
    {
        IntegerType i => i.Size,
        FloatingType f => f.Size,
        EnumType => CTypes.Int.Size,
        PointerType => CTypes.PointerSize,
        ArrayType { Length: { } n } a when SizeOf(a.Element.Type) is { } e => n * e,
        RecordType { IsComplete: true } r => r.Size,
        _ => null,
    };

    /// <summary>Alignment in bytes, or null for a type without a known size.</summary>
    public static int? AlignOf(CType t) => t switch
    {
        IntegerType i => i.Size,
        FloatingType f => f.Size,
        EnumType => CTypes.Int.Size,
        PointerType => CTypes.PointerSize,
        ArrayType a => AlignOf(a.Element.Type),
        RecordType { IsComplete: true } r => r.Alignment,
        _ => null,
    };

    // --- compatibility ----------------------------------------------------

    /// <summary>C type compatibility (C89 §3.1.2.6), ignoring top-level qualifiers.</summary>
    public static bool AreCompatible(CType a, CType b)
    {
        if (ReferenceEquals(a, b)) return true;

        return (a, b) switch
        {
            (IntegerType ia, IntegerType ib) => ia.Kind == ib.Kind,
            (FloatingType fa, FloatingType fb) => fa.Kind == fb.Kind,
            // An enum is compatible with its underlying integer type (implementation choice: int).
            (EnumType, IntegerType ib) => ib.Kind == IntegerKind.Int,
            (IntegerType ia, EnumType) => ia.Kind == IntegerKind.Int,
            (PointerType pa, PointerType pb) => AreCompatibleQualified(pa.Pointee, pb.Pointee),
            (ArrayType aa, ArrayType ab) =>
                AreCompatibleQualified(aa.Element, ab.Element)
                && (aa.Length is null || ab.Length is null || aa.Length == ab.Length),
            (FunctionType fa, FunctionType fb) => AreFunctionsCompatible(fa, fb),
            (VoidType, VoidType) => true,
            _ => false, // records/enums are compatible only by identity (handled above)
        };
    }

    public static bool AreCompatibleQualified(QualType a, QualType b)
        => a.Qualifiers == b.Qualifiers && AreCompatible(a.Type, b.Type);

    private static bool AreFunctionsCompatible(FunctionType a, FunctionType b)
    {
        if (!AreCompatibleQualified(a.ReturnType, b.ReturnType)) return false;
        // If either side is an old-style declaration, only the return type is constrained.
        if (!a.HasPrototype || !b.HasPrototype) return true;
        if (a.IsVariadic != b.IsVariadic) return false;
        if (a.Parameters.Count != b.Parameters.Count) return false;
        return a.Parameters.Zip(b.Parameters, (x, y) => AreCompatibleQualified(x.Type, y.Type)).All(ok => ok);
    }

    // --- conversions ------------------------------------------------------

    /// <summary>
    /// The integer promotions (C89 §3.2.1.1): types of rank lower than <c>int</c> — including
    /// <c>enum</c> — become <c>int</c> (all their values fit). Everything else is unchanged.
    /// </summary>
    public static CType Promote(CType t)
    {
        if (t is EnumType) return CTypes.Int;
        if (t is IntegerType i && i.Rank < CTypes.Int.Rank) return CTypes.Int;
        return t;
    }

    /// <summary>
    /// The usual arithmetic conversions (C89 §3.2.1.5): finds the common type of two arithmetic
    /// operands. Floating types win over integers; otherwise both are promoted and the result
    /// follows the rank/signedness rules.
    /// </summary>
    public static CType UsualArithmeticConversions(CType a, CType b)
    {
        if (a is FloatingType fa && b is FloatingType fb)
            return fa.Rank >= fb.Rank ? fa : fb;
        if (a is FloatingType) return a;
        if (b is FloatingType) return b;

        var pa = (IntegerType)Promote(a);
        var pb = (IntegerType)Promote(b);
        if (pa.Kind == pb.Kind) return pa;

        // Same signedness: higher rank wins.
        if (pa.IsSigned == pb.IsSigned)
            return pa.Rank >= pb.Rank ? pa : pb;

        // Different signedness: let (u, s) be the unsigned and signed operands.
        var u = pa.IsSigned ? pb : pa;
        var s = pa.IsSigned ? pa : pb;

        if (u.Rank >= s.Rank) return u;                 // unsigned rank >= signed rank → unsigned
        if (s.Size > u.Size) return s;                  // signed can represent all unsigned values → signed
        return CTypes.ToUnsigned(s);                    // otherwise → unsigned version of the signed type
    }
}
