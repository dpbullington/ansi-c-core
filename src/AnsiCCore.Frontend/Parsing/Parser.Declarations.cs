using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;

namespace AnsiCCore.Frontend.Parsing;

public sealed partial class Parser
{
    // ---------------------------------------------------------------------
    // External declarations: function definition vs. declaration
    // ---------------------------------------------------------------------

    private Node ParseExternalDeclaration()
    {
        var start = Current.Location;
        var specifiers = ParseDeclarationSpecifiers();

        // A lone "struct S { ... };" style declaration with no declarators.
        if (Match(TokenKind.Semicolon))
            return new Declaration(start, specifiers, []);

        var firstDeclarator = ParseDeclarator(allowAbstract: false);

        // Function definition: declarator is a function and the body (or K&R param decls) follows.
        bool looksLikeFunction = firstDeclarator is FunctionDeclarator;
        if (looksLikeFunction && (Check(TokenKind.LBrace) || IsDeclarationStart()))
        {
            RegisterDeclaredName(specifiers, firstDeclarator);
            return ParseFunctionDefinitionTail(start, specifiers, firstDeclarator);
        }

        // Otherwise it is a declaration; finish the init-declarator list.
        var initDeclarators = new List<InitDeclarator>();
        Initializer? init = Match(TokenKind.Equal) ? ParseInitializer() : null;
        initDeclarators.Add(new InitDeclarator(firstDeclarator.Location, firstDeclarator, init));
        RegisterDeclaredName(specifiers, firstDeclarator);

        while (Match(TokenKind.Comma))
        {
            var declarator = ParseDeclarator(allowAbstract: false);
            Initializer? initializer = Match(TokenKind.Equal) ? ParseInitializer() : null;
            initDeclarators.Add(new InitDeclarator(declarator.Location, declarator, initializer));
            RegisterDeclaredName(specifiers, declarator);
        }

        Expect(TokenKind.Semicolon, "';' after declaration");
        return new Declaration(start, specifiers, initDeclarators);
    }

    private FunctionDefinition ParseFunctionDefinitionTail(SourceLocation start, DeclarationSpecifiers specifiers, Declarator declarator)
    {
        // Optional K&R parameter declarations between the declarator and the body.
        var krDecls = new List<Declaration>();
        while (!Check(TokenKind.LBrace) && !AtEnd)
            krDecls.Add(ParseBlockDeclaration());

        var body = ParseCompoundStatement();
        return new FunctionDefinition(start, specifiers, declarator, krDecls, body);
    }

    /// <summary>Parses a declaration that is known to start here (block item or K&amp;R parameter).</summary>
    private Declaration ParseBlockDeclaration()
    {
        var start = Current.Location;
        var specifiers = ParseDeclarationSpecifiers();

        var initDeclarators = new List<InitDeclarator>();
        if (!Check(TokenKind.Semicolon))
        {
            do
            {
                var declarator = ParseDeclarator(allowAbstract: false);
                Initializer? initializer = Match(TokenKind.Equal) ? ParseInitializer() : null;
                initDeclarators.Add(new InitDeclarator(declarator.Location, declarator, initializer));
                RegisterDeclaredName(specifiers, declarator);
            }
            while (Match(TokenKind.Comma));
        }

        Expect(TokenKind.Semicolon, "';' after declaration");
        return new Declaration(start, specifiers, initDeclarators);
    }

    // ---------------------------------------------------------------------
    // Declaration specifiers
    // ---------------------------------------------------------------------

    private DeclarationSpecifiers ParseDeclarationSpecifiers()
    {
        var start = Current.Location;
        var storage = new List<TokenKind>();
        var qualifiers = new List<TokenKind>();
        var typeSpecifiers = new List<TypeSpecifier>();

        while (true)
        {
            var t = Current;
            if (IsStorageClassKeyword(t.Kind)) { storage.Add(t.Kind); Advance(); continue; }
            if (IsTypeQualifierKeyword(t.Kind)) { qualifiers.Add(t.Kind); Advance(); continue; }
            if (IsPrimitiveTypeKeyword(t.Kind)) { typeSpecifiers.Add(new PrimitiveTypeSpecifier(t.Location, t.Kind)); Advance(); continue; }

            if (t.Kind is TokenKind.KwStruct or TokenKind.KwUnion) { typeSpecifiers.Add(ParseStructOrUnion()); continue; }
            if (t.Kind == TokenKind.KwEnum) { typeSpecifiers.Add(ParseEnum()); continue; }

            // A typedef-name counts as a type specifier only if no type specifier is present yet.
            if (t.Kind == TokenKind.Identifier && typeSpecifiers.Count == 0 && IsTypedefName(t.Text))
            {
                typeSpecifiers.Add(new TypedefNameSpecifier(t.Location, t.Text));
                Advance();
                continue;
            }

            break;
        }

        if (storage.Count == 0 && qualifiers.Count == 0 && typeSpecifiers.Count == 0)
            throw Error("expected a type or declaration specifier");

        return new DeclarationSpecifiers(start, storage, qualifiers, typeSpecifiers);
    }

    private TypeSpecifier ParseStructOrUnion()
    {
        var kw = Advance(); // struct | union
        bool isUnion = kw.Kind == TokenKind.KwUnion;
        string? tag = Check(TokenKind.Identifier) ? Advance().Text : null;

        if (!Match(TokenKind.LBrace))
        {
            if (tag == null) throw Error("expected tag name or '{' after struct/union");
            return new StructOrUnionSpecifier(kw.Location, isUnion, tag, Members: null);
        }

        var members = new List<StructDeclaration>();
        while (!Check(TokenKind.RBrace) && !AtEnd)
            members.Add(ParseStructDeclaration());
        Expect(TokenKind.RBrace, "'}' to close struct/union body");

        return new StructOrUnionSpecifier(kw.Location, isUnion, tag, members);
    }

    private StructDeclaration ParseStructDeclaration()
    {
        var start = Current.Location;
        var specifiers = ParseDeclarationSpecifiers(); // specifier-qualifier-list (storage classes rejected later)

        var declarators = new List<StructDeclarator>();
        do
        {
            Declarator? declarator = null;
            if (!Check(TokenKind.Colon))
                declarator = ParseDeclarator(allowAbstract: false);

            Expression? bitWidth = Match(TokenKind.Colon) ? ParseConstantExpression() : null;
            declarators.Add(new StructDeclarator(start, declarator, bitWidth));
        }
        while (Match(TokenKind.Comma));

        Expect(TokenKind.Semicolon, "';' after struct member declaration");
        return new StructDeclaration(start, specifiers, declarators);
    }

    private TypeSpecifier ParseEnum()
    {
        var kw = Advance(); // enum
        string? tag = Check(TokenKind.Identifier) ? Advance().Text : null;

        if (!Match(TokenKind.LBrace))
        {
            if (tag == null) throw Error("expected tag name or '{' after enum");
            return new EnumSpecifier(kw.Location, tag, Enumerators: null);
        }

        var enumerators = new List<Enumerator>();
        while (!Check(TokenKind.RBrace) && !AtEnd)
        {
            var nameTok = Expect(TokenKind.Identifier, "enumerator name");
            Expression? value = Match(TokenKind.Equal) ? ParseConstantExpression() : null;
            enumerators.Add(new Enumerator(nameTok.Location, nameTok.Text, value));
            if (!Match(TokenKind.Comma)) break; // trailing comma is allowed; so is none
        }
        Expect(TokenKind.RBrace, "'}' to close enum body");

        return new EnumSpecifier(kw.Location, tag, enumerators);
    }

    // ---------------------------------------------------------------------
    // Declarators (concrete and abstract)
    // ---------------------------------------------------------------------

    private Declarator ParseDeclarator(bool allowAbstract)
    {
        // pointer* applies outermost: the first '*' parsed becomes the outermost node.
        var pointers = new List<(SourceLocation Loc, List<TokenKind> Quals)>();
        while (Check(TokenKind.Star))
        {
            var loc = Advance().Location;
            var quals = new List<TokenKind>();
            while (IsTypeQualifierKeyword(Kind)) quals.Add(Advance().Kind);
            pointers.Add((loc, quals));
        }

        var declarator = ParseDirectDeclarator(allowAbstract);

        for (int i = pointers.Count - 1; i >= 0; i--)
            declarator = new PointerDeclarator(pointers[i].Loc, pointers[i].Quals, declarator);

        return declarator;
    }

    private Declarator ParseDirectDeclarator(bool allowAbstract)
    {
        Declarator declarator;

        if (Check(TokenKind.LParen) && !StartsParameterList(Peek()))
        {
            // Grouping: '(' declarator ')'
            Advance();
            declarator = ParseDeclarator(allowAbstract);
            Expect(TokenKind.RParen, "')' in declarator");
        }
        else if (Check(TokenKind.Identifier))
        {
            var id = Advance();
            declarator = new IdentifierDeclarator(id.Location, id.Text);
        }
        else
        {
            if (!allowAbstract && !Check(TokenKind.LParen) && !Check(TokenKind.LBracket))
                throw Error("expected a declarator");
            declarator = new IdentifierDeclarator(Current.Location, Name: null); // abstract leaf
        }

        // Postfix array/function derivations, applied left to right (innermost first).
        while (true)
        {
            if (Check(TokenKind.LBracket))
            {
                var loc = Advance().Location;
                Expression? size = Check(TokenKind.RBracket) ? null : ParseConstantExpression();
                Expect(TokenKind.RBracket, "']' in array declarator");
                declarator = new ArrayDeclarator(loc, declarator, size);
            }
            else if (Check(TokenKind.LParen))
            {
                var loc = Advance().Location;
                var parameters = ParseFunctionParameters(loc);
                Expect(TokenKind.RParen, "')' in function declarator");
                declarator = new FunctionDeclarator(loc, declarator, parameters);
            }
            else
            {
                break;
            }
        }

        return declarator;
    }

    /// <summary>True when a '(' at a direct-declarator base position begins a parameter list rather than grouping.</summary>
    private bool StartsParameterList(in Token afterParen)
    {
        if (afterParen.Kind == TokenKind.RParen) return true;              // ()
        if (IsDeclarationSpecifierStart(afterParen)) return true;          // (int ...), (void), (MyType ...)
        return false;                                                     // (*x), (x), ( (…) ) → grouping
    }

    private FunctionParameters ParseFunctionParameters(SourceLocation loc)
    {
        if (Check(TokenKind.RParen))
            return new FunctionParameters(loc, ParameterKind.Empty, [], IsVariadic: false, []);

        // K&R identifier list: a bare identifier that is not a typedef-name (hence not a type).
        if (Check(TokenKind.Identifier) && !IsTypedefName(Current.Text))
        {
            var names = new List<string> { Advance().Text };
            while (Match(TokenKind.Comma))
                names.Add(Expect(TokenKind.Identifier, "parameter name").Text);
            return new FunctionParameters(loc, ParameterKind.IdentifierList, [], IsVariadic: false, names);
        }

        var parameters = new List<ParameterDeclaration>();
        bool variadic = false;
        do
        {
            if (Check(TokenKind.Ellipsis)) { Advance(); variadic = true; break; }
            parameters.Add(ParseParameterDeclaration());
        }
        while (Match(TokenKind.Comma));

        // "(void)" is the empty prototype.
        if (!variadic && parameters.Count == 1 && IsVoidParameter(parameters[0]))
            parameters.Clear();

        return new FunctionParameters(loc, ParameterKind.Prototype, parameters, variadic, []);
    }

    private static bool IsVoidParameter(ParameterDeclaration p)
        => p.Declarator is null
           && p.Specifiers.StorageClasses.Count == 0
           && p.Specifiers.TypeQualifiers.Count == 0
           && p.Specifiers.TypeSpecifiers is [PrimitiveTypeSpecifier { Keyword: TokenKind.KwVoid }];

    private ParameterDeclaration ParseParameterDeclaration()
    {
        var start = Current.Location;
        var specifiers = ParseDeclarationSpecifiers();

        // A declarator (concrete or abstract) is present unless the param ends here.
        Declarator? declarator = null;
        if (!Check(TokenKind.Comma) && !Check(TokenKind.RParen))
            declarator = ParseDeclarator(allowAbstract: true);

        return new ParameterDeclaration(start, specifiers, declarator);
    }

    // ---------------------------------------------------------------------
    // Type-names and initializers
    // ---------------------------------------------------------------------

    private TypeName ParseTypeName()
    {
        var start = Current.Location;
        var specifiers = ParseDeclarationSpecifiers();
        Declarator? abstractDeclarator = null;
        if (!Check(TokenKind.RParen))
            abstractDeclarator = ParseDeclarator(allowAbstract: true);
        return new TypeName(start, specifiers, abstractDeclarator);
    }

    private Initializer ParseInitializer()
    {
        if (Check(TokenKind.LBrace))
        {
            var loc = Advance().Location;
            var items = new List<Initializer>();
            while (!Check(TokenKind.RBrace) && !AtEnd)
            {
                items.Add(ParseInitializer());
                if (!Match(TokenKind.Comma)) break; // trailing comma allowed
            }
            Expect(TokenKind.RBrace, "'}' to close initializer list");
            return new InitializerList(loc, items);
        }

        var expr = ParseAssignment();
        return new ExpressionInitializer(expr.Location, expr);
    }

    // ---------------------------------------------------------------------
    // typedef-name registration
    // ---------------------------------------------------------------------

    private void RegisterDeclaredName(DeclarationSpecifiers specifiers, Declarator declarator)
    {
        string? name = ExtractName(declarator);
        if (name != null)
            DeclareName(name, specifiers.IsTypedef);
    }

    /// <summary>Walks a declarator chain to the identifier leaf and returns its name (null if abstract).</summary>
    private static string? ExtractName(Declarator declarator) => declarator switch
    {
        IdentifierDeclarator id => id.Name,
        PointerDeclarator p => ExtractName(p.Inner),
        ArrayDeclarator a => ExtractName(a.Inner),
        FunctionDeclarator f => ExtractName(f.Inner),
        _ => null,
    };
}
