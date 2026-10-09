namespace AnsiCCore.Frontend.Lexing;

/// <summary>
/// The lexical category of a <see cref="Token"/>. Covers the full ANSI C (C89) token set:
/// the 32 keywords, identifiers, the four constant/literal classes, and every punctuator
/// (including the preprocessor punctuators <c>#</c> and <c>##</c>).
/// </summary>
public enum TokenKind
{
    // Special
    Unknown,
    EndOfFile,

    // Identifier (non-keyword)
    Identifier,

    // Constants / literals. The lexer classifies and captures the raw lexeme; numeric
    // value and escape-sequence decoding are deferred to later stages (clang-style).
    IntegerConstant,
    FloatingConstant,
    CharacterConstant,
    StringLiteral,

    // Keywords (C89, 32 of them)
    KwAuto, KwBreak, KwCase, KwChar, KwConst, KwContinue, KwDefault, KwDo,
    KwDouble, KwElse, KwEnum, KwExtern, KwFloat, KwFor, KwGoto, KwIf,
    KwInt, KwLong, KwRegister, KwReturn, KwShort, KwSigned, KwSizeof, KwStatic,
    KwStruct, KwSwitch, KwTypedef, KwUnion, KwUnsigned, KwVoid, KwVolatile, KwWhile,

    // Punctuators
    LBracket,            // [
    RBracket,            // ]
    LParen,              // (
    RParen,              // )
    LBrace,              // {
    RBrace,              // }
    Dot,                 // .
    Arrow,               // ->
    PlusPlus,            // ++
    MinusMinus,          // --
    Ampersand,           // &
    Star,                // *
    Plus,                // +
    Minus,               // -
    Tilde,               // ~
    Bang,                // !
    Slash,               // /
    Percent,             // %
    LessLess,            // <<
    GreaterGreater,      // >>
    Less,                // <
    Greater,             // >
    LessEqual,           // <=
    GreaterEqual,        // >=
    EqualEqual,          // ==
    BangEqual,           // !=
    Caret,               // ^
    Pipe,                // |
    AmpAmp,              // &&
    PipePipe,            // ||
    Question,            // ?
    Colon,               // :
    Semicolon,           // ;
    Ellipsis,            // ...
    Equal,               // =
    StarEqual,           // *=
    SlashEqual,          // /=
    PercentEqual,        // %=
    PlusEqual,           // +=
    MinusEqual,          // -=
    LessLessEqual,       // <<=
    GreaterGreaterEqual, // >>=
    AmpEqual,            // &=
    CaretEqual,          // ^=
    PipeEqual,           // |=
    Comma,               // ,
    Hash,                // #
    HashHash,            // ##
}
