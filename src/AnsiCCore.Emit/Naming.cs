namespace AnsiCCore.Emit;

/// <summary>
/// Produces C# identifiers from C names. C identifiers are already valid C# identifiers, so the
/// only hazard is a C name that happens to be a C# keyword (<c>string</c>, <c>lock</c>, …); those
/// are escaped with a leading <c>@</c> so the output stays readable and un-mangled otherwise.
/// </summary>
internal static class Naming
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
    };

    public static string Safe(string name) => Keywords.Contains(name) ? "@" + name : name;
}
