using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;

namespace AnsiCCore.Frontend.Ast;

// ---------------------------------------------------------------------------
// Declaration specifiers and type specifiers
// ---------------------------------------------------------------------------

/// <summary>
/// The leading specifiers of a declaration. Storage classes and type qualifiers are kept as
/// their keyword <see cref="TokenKind"/>s; <see cref="TypeSpecifiers"/> preserves the sequence
/// so combinations such as <c>unsigned long int</c> can be resolved later.
/// </summary>
public sealed record DeclarationSpecifiers(
    SourceLocation Location,
    IReadOnlyList<TokenKind> StorageClasses,
    IReadOnlyList<TokenKind> TypeQualifiers,
    IReadOnlyList<TypeSpecifier> TypeSpecifiers) : Node(Location)
{
    public bool IsTypedef => StorageClasses.Contains(TokenKind.KwTypedef);
}

/// <summary>Base of the type-specifier portion of a declaration.</summary>
public abstract record TypeSpecifier(SourceLocation Location) : Node(Location);

/// <summary>A built-in type keyword: <c>void char short int long float double signed unsigned</c>.</summary>
public sealed record PrimitiveTypeSpecifier(SourceLocation Location, TokenKind Keyword) : TypeSpecifier(Location);

/// <summary>A <c>typedef</c>-name used as a type specifier.</summary>
public sealed record TypedefNameSpecifier(SourceLocation Location, string Name) : TypeSpecifier(Location);

/// <summary>
/// A <c>struct</c>/<c>union</c> specifier. <see cref="Members"/> is null for an incomplete
/// reference (<c>struct S x;</c>) and non-null (possibly empty) when a body <c>{ ... }</c> is present.
/// </summary>
public sealed record StructOrUnionSpecifier(
    SourceLocation Location,
    bool IsUnion,
    string? Tag,
    IReadOnlyList<StructDeclaration>? Members) : TypeSpecifier(Location);

/// <summary>An <c>enum</c> specifier. <see cref="Enumerators"/> is null for an incomplete reference.</summary>
public sealed record EnumSpecifier(
    SourceLocation Location,
    string? Tag,
    IReadOnlyList<Enumerator>? Enumerators) : TypeSpecifier(Location);

/// <summary>A single <c>enum</c> constant, with an optional explicit value.</summary>
public sealed record Enumerator(SourceLocation Location, string Name, Expression? Value) : Node(Location);

/// <summary>One member group inside a struct/union body (a specifier-qualifier list + declarators).</summary>
public sealed record StructDeclaration(
    SourceLocation Location,
    DeclarationSpecifiers Specifiers,
    IReadOnlyList<StructDeclarator> Declarators) : Node(Location);

/// <summary>A struct member declarator, optionally a bit-field (<c>declarator : width</c>).</summary>
public sealed record StructDeclarator(SourceLocation Location, Declarator? Declarator, Expression? BitWidth) : Node(Location);

// ---------------------------------------------------------------------------
// Declarators (structural; types resolved in Semantics)
// ---------------------------------------------------------------------------

/// <summary>
/// Base of the declarator tree. The outermost node is the last-applied type constructor, so
/// Semantics reconstructs the C type by folding the declaration's base type through the tree
/// from the outside in (C89 §3.5.4).
/// </summary>
public abstract record Declarator(SourceLocation Location) : Node(Location);

/// <summary>The declared name (null for an abstract declarator, as in a type-name or unnamed parameter).</summary>
public sealed record IdentifierDeclarator(SourceLocation Location, string? Name) : Declarator(Location);

/// <summary>A pointer derivation <c>* qualifiers inner</c>.</summary>
public sealed record PointerDeclarator(SourceLocation Location, IReadOnlyList<TokenKind> Qualifiers, Declarator Inner) : Declarator(Location);

/// <summary>An array derivation <c>inner [ size ]</c>; <see cref="Size"/> is null for <c>[]</c>.</summary>
public sealed record ArrayDeclarator(SourceLocation Location, Declarator Inner, Expression? Size) : Declarator(Location);

/// <summary>A function derivation <c>inner ( parameters )</c>.</summary>
public sealed record FunctionDeclarator(SourceLocation Location, Declarator Inner, FunctionParameters Parameters) : Declarator(Location);

/// <summary>Distinguishes the three shapes a function declarator's parameter list can take.</summary>
public enum ParameterKind
{
    /// <summary><c>()</c> — no information about parameters (old-style).</summary>
    Empty,
    /// <summary>A prototype list, e.g. <c>(int, char *)</c>; <c>(void)</c> is the empty prototype.</summary>
    Prototype,
    /// <summary>A K&amp;R identifier list, e.g. <c>(argc, argv)</c>.</summary>
    IdentifierList,
}

/// <summary>A function declarator's parameter list.</summary>
public sealed record FunctionParameters(
    SourceLocation Location,
    ParameterKind Kind,
    IReadOnlyList<ParameterDeclaration> Parameters,
    bool IsVariadic,
    IReadOnlyList<string> Identifiers) : Node(Location);

/// <summary>One prototype parameter: specifiers plus an optional (possibly abstract) declarator.</summary>
public sealed record ParameterDeclaration(SourceLocation Location, DeclarationSpecifiers Specifiers, Declarator? Declarator) : Node(Location);

// ---------------------------------------------------------------------------
// Declarations, initializers, type-names
// ---------------------------------------------------------------------------

/// <summary>A declaration: shared specifiers plus a list of declarators with optional initializers.</summary>
public sealed record Declaration(
    SourceLocation Location,
    DeclarationSpecifiers Specifiers,
    IReadOnlyList<InitDeclarator> InitDeclarators) : Node(Location);

/// <summary>A declarator paired with its optional initializer.</summary>
public sealed record InitDeclarator(SourceLocation Location, Declarator Declarator, Initializer? Initializer) : Node(Location);

/// <summary>Base of initializers.</summary>
public abstract record Initializer(SourceLocation Location) : Node(Location);

/// <summary>A scalar initializer <c>= expression</c>.</summary>
public sealed record ExpressionInitializer(SourceLocation Location, Expression Expression) : Initializer(Location);

/// <summary>A brace-enclosed initializer list <c>{ a, b, ... }</c>.</summary>
public sealed record InitializerList(SourceLocation Location, IReadOnlyList<Initializer> Items) : Initializer(Location);

/// <summary>A type-name (used by casts and <c>sizeof</c>): specifiers plus an optional abstract declarator.</summary>
public sealed record TypeName(SourceLocation Location, DeclarationSpecifiers Specifiers, Declarator? Declarator) : Node(Location);

/// <summary>A function definition, including any K&amp;R parameter declarations preceding the body.</summary>
public sealed record FunctionDefinition(
    SourceLocation Location,
    DeclarationSpecifiers Specifiers,
    Declarator Declarator,
    IReadOnlyList<Declaration> ParameterDeclarations,
    CompoundStatement Body) : Node(Location);
