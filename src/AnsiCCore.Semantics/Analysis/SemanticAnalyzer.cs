using AnsiCCore.Frontend.Ast;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;
using AnsiCCore.Semantics.Symbols;
using AnsiCCore.Semantics.Types;

namespace AnsiCCore.Semantics.Analysis;

/// <summary>
/// Walks a parsed <see cref="TranslationUnit"/> and performs semantic analysis: it builds the
/// scope/symbol tables, resolves every declaration's C type (folding declarators per C89
/// §3.5.4), completes struct/union/enum tags, and reports declaration-level diagnostics
/// (redeclaration conflicts, unknown types, undeclared identifiers, and the like).
/// </summary>
/// <remarks>
/// Expression <em>type-checking</em> (operand compatibility, applying the usual arithmetic
/// conversions to each expression) is a later slice; the conversion machinery it will use
/// already lives in <see cref="TypeOperations"/>. This pass resolves identifier <em>uses</em>
/// against the symbol table so scopes are exercised end to end.
/// </remarks>
public sealed partial class SemanticAnalyzer
{
    private readonly DiagnosticBag _diagnostics;
    private readonly Dictionary<Node, Symbol> _symbols = new();
    private readonly Dictionary<Expression, ExpressionType> _expressionTypes = new();
    private readonly Dictionary<TypeName, QualType> _typeNames = new();
    private readonly Scope _global;
    private Scope _current;

    // Per-function label namespace (C89 §3.1.2.3: labels have function scope).
    private readonly Dictionary<string, SourceLocation> _definedLabels = new(StringComparer.Ordinal);
    private readonly List<(string Label, SourceLocation Location)> _gotos = [];
    private QualType? _currentFunctionReturn;

    private SemanticAnalyzer(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics;
        _global = new Scope(ScopeKind.File, parent: null);
        _current = _global;
    }

    /// <summary>Analyzes a translation unit, returning the populated <see cref="SemanticModel"/>.</summary>
    public static SemanticModel Analyze(TranslationUnit unit, DiagnosticBag? diagnostics = null)
    {
        var bag = diagnostics ?? new DiagnosticBag();
        var analyzer = new SemanticAnalyzer(bag);
        analyzer.Run(unit);

        var model = new SemanticModel(analyzer._global, bag.ToList());
        foreach (var (node, sym) in analyzer._symbols)
            model.RecordSymbol(node, sym);
        foreach (var (expr, type) in analyzer._expressionTypes)
            model.RecordExpressionType(expr, type);
        foreach (var (node, type) in analyzer._typeNames)
            model.RecordTypeName(node, type);
        return model;
    }

    private void Run(TranslationUnit unit)
    {
        foreach (var item in unit.Items)
        {
            switch (item)
            {
                case FunctionDefinition f: AnalyzeFunctionDefinition(f); break;
                case Declaration d: AnalyzeDeclaration(d, fileScope: true); break;
            }
        }
    }

    // --- diagnostics ------------------------------------------------------

    private void Error(SourceLocation loc, string message) => _diagnostics.Error(loc, message);
    private void Warn(SourceLocation loc, string message) => _diagnostics.Warning(loc, message);

    // --- declarations -----------------------------------------------------

    private void AnalyzeDeclaration(Declaration d, bool fileScope)
    {
        var baseType = ResolveSpecifiers(d.Specifiers);
        var storage = ResolveStorage(d.Specifiers);
        bool isTypedef = d.Specifiers.IsTypedef;

        if (d.InitDeclarators.Count == 0)
            return; // a bare tag/type declaration; the tag was handled by ResolveSpecifiers

        foreach (var init in d.InitDeclarators)
        {
            var (name, type) = ResolveDeclarator(baseType, init.Declarator);
            if (name is null)
            {
                Error(init.Location, "declaration does not declare anything");
                continue;
            }

            Symbol symbol = isTypedef
                ? new Symbol(name, SymbolKind.Typedef, type, StorageClass.None, init.Location)
                : new Symbol(name, type.Type is FunctionType ? SymbolKind.Function : SymbolKind.Variable, type, storage, init.Location);

            DeclareOrdinaryChecked(symbol, fileScope);
            _symbols[init] = symbol;

            if (init.Initializer is not null)
            {
                if (isTypedef) Error(init.Location, $"typedef '{name}' cannot have an initializer");
                else if (type.Type is FunctionType) Error(init.Location, $"'{name}' is a function and cannot be initialized");
                AnalyzeInitializer(init.Initializer);
            }
        }
    }

    private StorageClass ResolveStorage(DeclarationSpecifiers specs)
    {
        StorageClass result = StorageClass.None;
        int count = 0;
        foreach (var sc in specs.StorageClasses)
        {
            if (sc == TokenKind.KwTypedef) continue; // modeled as SymbolKind.Typedef
            count++;
            result = sc switch
            {
                TokenKind.KwExtern => StorageClass.Extern,
                TokenKind.KwStatic => StorageClass.Static,
                TokenKind.KwAuto => StorageClass.Auto,
                TokenKind.KwRegister => StorageClass.Register,
                _ => result,
            };
        }
        if (count > 1)
            Warn(specs.Location, "multiple storage-class specifiers in declaration");
        return result;
    }

    /// <summary>Declares a symbol in the current scope, checking C's redeclaration rules.</summary>
    private void DeclareOrdinaryChecked(Symbol symbol, bool fileScope)
    {
        var existing = _current.LookupOrdinaryLocal(symbol.Name);
        if (existing is null)
        {
            _current.DeclareOrdinary(symbol);
            return;
        }

        // At file scope, compatible re-declarations of the same object/function are allowed.
        if (fileScope
            && existing.Kind == symbol.Kind
            && existing.Kind is SymbolKind.Variable or SymbolKind.Function or SymbolKind.Typedef
            && TypeOperations.AreCompatible(existing.Type.Type, symbol.Type.Type))
        {
            if (symbol.IsDefined) _current.DeclareOrdinary(symbol); // a definition supersedes a prior declaration
            return;
        }

        Error(symbol.Location, $"redeclaration of '{symbol.Name}'");
    }

    // --- record layout ----------------------------------------------------

    /// <summary>
    /// Computes a struct/union layout with natural alignment, setting each field's byte offset
    /// (and bit offset for bit-fields) and returning the overall size and alignment. Bit-fields
    /// are packed little-endian into storage units of their declared type; one that would straddle
    /// a unit boundary starts a new unit, and a zero-width bit-field rounds to the next unit — a
    /// common (x86-64-like, implementation-defined) model.
    /// </summary>
    private static (long Size, int Alignment) ComputeLayout(IReadOnlyList<Field> fields, bool isUnion)
    {
        int align = 1;
        long bitCursor = 0; // bits from the start of the aggregate

        foreach (var field in fields)
        {
            int fieldAlign = TypeOperations.AlignOf(field.Type.Type) ?? 1;
            long fieldSize = TypeOperations.SizeOf(field.Type.Type) ?? 0;
            align = Math.Max(align, fieldAlign);

            if (isUnion)
            {
                field.Offset = 0;
                field.BitOffset = 0;
                bitCursor = Math.Max(bitCursor, fieldSize * 8);
                continue;
            }

            if (field.BitWidth is { } width)
            {
                long unitBits = fieldSize * 8;
                if (width == 0) { bitCursor = RoundUp(bitCursor, unitBits); continue; } // force next unit
                if (bitCursor % unitBits + width > unitBits) bitCursor = RoundUp(bitCursor, unitBits);
                field.BitOffset = (int)(bitCursor % unitBits);
                field.Offset = (bitCursor - field.BitOffset) / 8;
                bitCursor += width;
            }
            else
            {
                long byteOffset = RoundUp(RoundUp(bitCursor, 8) / 8, fieldAlign);
                field.Offset = byteOffset;
                field.BitOffset = 0;
                bitCursor = (byteOffset + fieldSize) * 8;
            }
        }

        long size = RoundUp(RoundUp(bitCursor, 8) / 8, align);
        return (size, align);
    }

    private static long RoundUp(long value, long alignment)
        => alignment <= 1 ? value : (value + alignment - 1) / alignment * alignment;
}
