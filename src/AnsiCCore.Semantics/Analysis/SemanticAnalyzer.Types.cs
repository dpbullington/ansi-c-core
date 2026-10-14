using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;
using AnsiCCore.Semantics.Symbols;
using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Semantics.Analysis;

public sealed partial class SemanticAnalyzer
{
    // --- declaration specifiers -> base QualType --------------------------

    private QualType ResolveSpecifiers(DeclarationSpecifiers specs)
    {
        var quals = ResolveQualifiers(specs.TypeQualifiers);

        // struct/union/enum/typedef-name: exactly one may appear, and not alongside primitives.
        TypeSpecifier? named = specs.TypeSpecifiers.FirstOrDefault(
            ts => ts is StructOrUnionSpecifier or EnumSpecifier or TypedefNameSpecifier);

        if (named is not null)
        {
            if (specs.TypeSpecifiers.Count > 1)
                Error(specs.Location, "too many type specifiers in declaration");

            CType type = named switch
            {
                StructOrUnionSpecifier s => ResolveRecord(s),
                EnumSpecifier e => ResolveEnum(e),
                TypedefNameSpecifier t => ResolveTypedefName(t, out var tq) is { } resolved ? Requalify(resolved, ref quals, tq) : CTypes.Int,
                _ => CTypes.Int,
            };
            return new QualType(type, quals);
        }

        var primitives = specs.TypeSpecifiers.OfType<PrimitiveTypeSpecifier>().Select(p => p.Keyword).ToList();
        return new QualType(CombinePrimitives(primitives, specs.Location), quals);
    }

    private static CType Requalify(CType resolved, ref TypeQualifiers quals, TypeQualifiers typedefQuals)
    {
        quals |= typedefQuals; // a typedef can itself be qualified; combine with the use-site qualifiers
        return resolved;
    }

    private CType? ResolveTypedefName(TypedefNameSpecifier t, out TypeQualifiers typedefQuals)
    {
        typedefQuals = TypeQualifiers.None;
        var sym = _current.LookupOrdinary(t.Name);
        if (sym is { Kind: SymbolKind.Typedef })
        {
            typedefQuals = sym.Type.Qualifiers;
            return sym.Type.Type;
        }
        Error(t.Location, $"unknown type name '{t.Name}'");
        return null;
    }

    private static TypeQualifiers ResolveQualifiers(IReadOnlyList<TokenKind> qualifiers)
    {
        var q = TypeQualifiers.None;
        foreach (var k in qualifiers)
        {
            if (k == TokenKind.KwConst) q |= TypeQualifiers.Const;
            else if (k == TokenKind.KwVolatile) q |= TypeQualifiers.Volatile;
        }
        return q;
    }

    /// <summary>Interprets a multiset of primitive type keywords as a single basic type (C89 §3.5.2).</summary>
    private CType CombinePrimitives(List<TokenKind> keywords, SourceLocation loc)
    {
        if (keywords.Count == 0)
            return CTypes.Int; // implicit int

        int Count(TokenKind k) => keywords.Count(x => x == k);
        int nVoid = Count(TokenKind.KwVoid), nChar = Count(TokenKind.KwChar);
        int nShort = Count(TokenKind.KwShort), nInt = Count(TokenKind.KwInt), nLong = Count(TokenKind.KwLong);
        int nFloat = Count(TokenKind.KwFloat), nDouble = Count(TokenKind.KwDouble);
        int nSigned = Count(TokenKind.KwSigned), nUnsigned = Count(TokenKind.KwUnsigned);

        CType Fail()
        {
            Error(loc, "invalid combination of type specifiers");
            return CTypes.Int;
        }

        if (nSigned > 0 && nUnsigned > 0) return Fail();
        bool unsigned = nUnsigned > 0;

        if (nVoid > 0)
            return keywords.Count == nVoid && nVoid == 1 ? CTypes.Void : Fail();

        if (nFloat > 0)
            return keywords.Count == 1 ? CTypes.Float : Fail();

        if (nDouble > 0)
        {
            if (nDouble == 1 && nLong == 0 && keywords.Count == 1) return CTypes.Double;
            if (nDouble == 1 && nLong == 1 && keywords.Count == 2) return CTypes.LongDouble;
            return Fail();
        }

        if (nChar > 0)
        {
            if (nChar != 1 || nShort > 0 || nLong > 0 || nInt > 0) return Fail();
            return unsigned ? CTypes.UnsignedChar : (nSigned > 0 ? CTypes.SignedChar : CTypes.Char);
        }

        if (nShort > 0)
        {
            if (nShort != 1 || nLong > 0 || nInt > 1) return Fail();
            return unsigned ? CTypes.UnsignedShort : CTypes.Short;
        }

        if (nLong > 0)
        {
            if (nLong != 1 || nInt > 1) return Fail();   // no "long long" in C89
            return unsigned ? CTypes.UnsignedLong : CTypes.Long;
        }

        // Only int and/or signed/unsigned remain.
        if (nInt > 1) return Fail();
        return unsigned ? CTypes.UnsignedInt : CTypes.Int;
    }

    // --- struct / union / enum --------------------------------------------

    private CType ResolveRecord(StructOrUnionSpecifier s)
    {
        // Forward reference (no body): find an existing tag or forward-declare one.
        if (s.Members is null)
        {
            if (s.Tag is null) { Error(s.Location, "anonymous struct/union must have a body"); return new RecordType(s.IsUnion, null); }
            if (_current.LookupTag(s.Tag) is RecordType existing && existing.IsUnion == s.IsUnion)
                return existing;
            var forward = new RecordType(s.IsUnion, s.Tag);
            _current.DeclareTag(s.Tag, forward);
            return forward;
        }

        // Definition (with body). Reuse a prior forward declaration in this scope if present.
        RecordType record;
        if (s.Tag is not null && _current.LookupTagLocal(s.Tag) is RecordType local && !local.IsComplete && local.IsUnion == s.IsUnion)
            record = local;
        else
        {
            record = new RecordType(s.IsUnion, s.Tag);
            if (s.Tag is not null) _current.DeclareTag(s.Tag, record); // declare before members for self-reference
        }

        if (record.IsComplete)
            Error(s.Location, $"redefinition of '{record.Display}'");

        var fields = new List<Field>();
        foreach (var member in s.Members)
        {
            var memberBase = ResolveSpecifiers(member.Specifiers);
            foreach (var sd in member.Declarators)
            {
                QualType fieldType = memberBase;
                string? fieldName = null;
                if (sd.Declarator is not null)
                    (fieldName, fieldType) = ResolveDeclarator(memberBase, sd.Declarator);

                int? bitWidth = null;
                if (sd.BitWidth is not null && EvalConst(sd.BitWidth) is { } w)
                    bitWidth = (int)w;

                fields.Add(new Field(fieldName, fieldType, bitWidth));
            }
        }

        var (size, align) = ComputeLayout(fields, s.IsUnion);
        record.Complete(fields, size, align);
        return record;
    }

    private CType ResolveEnum(EnumSpecifier e)
    {
        if (e.Enumerators is null)
        {
            if (e.Tag is null) { Error(e.Location, "anonymous enum must have a body"); return new EnumType(null); }
            if (_current.LookupTag(e.Tag) is EnumType existing) return existing;
            var forward = new EnumType(e.Tag);
            _current.DeclareTag(e.Tag, forward);
            return forward;
        }

        var enumType = new EnumType(e.Tag);
        if (e.Tag is not null) _current.DeclareTag(e.Tag, enumType);

        var constants = new List<(string, long)>();
        long next = 0;
        foreach (var enumerator in e.Enumerators)
        {
            long value = next;
            if (enumerator.Value is not null)
            {
                if (EvalConst(enumerator.Value) is { } v) value = v;
                else Error(enumerator.Location, $"enumerator value for '{enumerator.Name}' is not an integer constant");
            }

            var constant = new Symbol(enumerator.Name, SymbolKind.EnumConstant, CTypes.Int, StorageClass.None, enumerator.Location)
            {
                EnumValue = value,
            };
            DeclareOrdinaryChecked(constant, _current.IsFileScope);
            constants.Add((enumerator.Name, value));
            next = value + 1;
        }

        enumType.Complete(constants);
        return enumType;
    }

    private long? EvalConst(Expression expr)
    {
        var evaluator = new ConstantEvaluator(_current, ResolveTypeName);
        return evaluator.TryEvaluate(expr, out long value) ? value : null;
    }

    // --- declarators ------------------------------------------------------

    /// <summary>Folds a declarator over a base type per C89 §3.5.4, returning the declared name and type.</summary>
    private (string? Name, QualType Type) ResolveDeclarator(QualType baseType, Declarator declarator)
    {
        switch (declarator)
        {
            case IdentifierDeclarator id:
                return (id.Name, baseType);

            case PointerDeclarator p:
            {
                var pointer = new QualType(new PointerType(baseType), ResolveQualifiers(p.Qualifiers));
                return ResolveDeclarator(pointer, p.Inner);
            }

            case ArrayDeclarator a:
            {
                long? length = a.Size is null ? null : EvalConst(a.Size);
                var array = new QualType(new ArrayType(baseType, length));
                return ResolveDeclarator(array, a.Inner);
            }

            case FunctionDeclarator f:
            {
                var function = new QualType(ResolveFunctionType(baseType, f.Parameters));
                return ResolveDeclarator(function, f.Inner);
            }

            default:
                return (null, baseType);
        }
    }

    private FunctionType ResolveFunctionType(QualType returnType, FunctionParameters parameters)
    {
        switch (parameters.Kind)
        {
            case ParameterKind.Empty:
                return new FunctionType(returnType, [], isVariadic: false, hasPrototype: false);

            case ParameterKind.IdentifierList:
                // K&R: parameter types are given separately in the definition; unknown here.
                return new FunctionType(returnType, [], isVariadic: false, hasPrototype: false);

            default:
            {
                var list = new List<Parameter>();
                foreach (var p in parameters.Parameters)
                {
                    var pBase = ResolveSpecifiers(p.Specifiers);
                    string? name = null;
                    QualType pType = pBase;
                    if (p.Declarator is not null)
                        (name, pType) = ResolveDeclarator(pBase, p.Declarator);
                    list.Add(new Parameter(AdjustParameterType(pType), name));
                }
                return new FunctionType(returnType, list, parameters.IsVariadic, hasPrototype: true);
            }
        }
    }

    /// <summary>Parameters of array/function type are adjusted to pointer type (C89 §3.7.1).</summary>
    private static QualType AdjustParameterType(QualType type) => type.Type switch
    {
        ArrayType a => new QualType(new PointerType(a.Element), type.Qualifiers),
        FunctionType => new QualType(new PointerType(type)),
        _ => type,
    };

    // --- type-names (casts, sizeof) ---------------------------------------

    private QualType ResolveTypeName(TypeName typeName)
    {
        var baseType = ResolveSpecifiers(typeName.Specifiers);
        if (typeName.Declarator is null) return baseType;
        var (_, type) = ResolveDeclarator(baseType, typeName.Declarator);
        return type;
    }
}
