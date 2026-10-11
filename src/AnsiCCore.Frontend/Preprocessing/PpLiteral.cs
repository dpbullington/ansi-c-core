namespace AnsiCCore.Frontend.Preprocessing;

/// <summary>
/// Decodes integer and character constant lexemes to their <see cref="PpValue"/> for use in
/// <c>#if</c> evaluation. Deliberately lenient: on a malformed lexeme it reports an error
/// message via its out parameter and returns a best-effort value so evaluation can continue.
/// </summary>
internal static class PpLiteral
{
    public static PpValue DecodeInteger(string lexeme, out string? error)
    {
        error = null;

        // Strip integer suffix (u/U/l/L in any order/combination).
        bool unsigned = false;
        int end = lexeme.Length;
        while (end > 0 && lexeme[end - 1] is 'u' or 'U' or 'l' or 'L')
        {
            if (lexeme[end - 1] is 'u' or 'U') unsigned = true;
            end--;
        }

        string core = lexeme[..end];
        ulong value;

        if (core.Length >= 2 && core[0] == '0' && (core[1] is 'x' or 'X'))
        {
            value = ParseRadix(core.AsSpan(2), 16, out error);
        }
        else if (core.Length >= 2 && core[0] == '0')
        {
            value = ParseRadix(core.AsSpan(1), 8, out error);
        }
        else
        {
            value = ParseRadix(core.AsSpan(), 10, out error);
        }

        // A decimal/large constant that does not fit in a signed long is unsigned (C89 §3.1.3.2).
        if (value > long.MaxValue) unsigned = true;

        return new PpValue(unchecked((long)value), unsigned);
    }

    private static ulong ParseRadix(ReadOnlySpan<char> digits, int radix, out string? error)
    {
        error = null;
        if (digits.IsEmpty)
        {
            error = "integer constant has no digits";
            return 0;
        }

        ulong value = 0;
        foreach (char c in digits)
        {
            int d = DigitValue(c);
            if (d < 0 || d >= radix)
            {
                error = $"invalid digit '{c}' in {RadixName(radix)} constant";
                return value;
            }
            value = unchecked(value * (ulong)radix + (ulong)d);
        }
        return value;
    }

    private static int DigitValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private static string RadixName(int radix) => radix switch
    {
        8 => "octal",
        16 => "hexadecimal",
        _ => "decimal",
    };

    public static PpValue DecodeCharacter(string lexeme, out string? error)
    {
        error = null;

        // Drop an optional wide prefix, then the surrounding quotes.
        int i = 0;
        if (i < lexeme.Length && lexeme[i] == 'L') i++;
        if (i >= lexeme.Length || lexeme[i] != '\'')
        {
            error = "malformed character constant";
            return PpValue.Zero;
        }
        i++; // opening quote
        int contentEnd = lexeme.Length - (lexeme.EndsWith('\'') ? 1 : 0);

        long value = 0;
        int count = 0;
        while (i < contentEnd)
        {
            int code = DecodeOneChar(lexeme, ref i, contentEnd, ref error);
            value = (value << 8) | (uint)(code & 0xFF);
            count++;
        }

        if (count == 0)
            error ??= "empty character constant";

        // A single-character constant has type int with the character's value.
        return PpValue.Signed(count == 1 ? (sbyte)value : value);
    }

    private static int DecodeOneChar(string s, ref int i, int end, ref string? error)
    {
        char c = s[i++];
        if (c != '\\')
            return c;

        if (i >= end)
        {
            error ??= "incomplete escape sequence";
            return '\\';
        }

        char e = s[i++];
        switch (e)
        {
            case 'n': return '\n';
            case 't': return '\t';
            case 'r': return '\r';
            case 'v': return '\v';
            case 'f': return '\f';
            case 'b': return '\b';
            case 'a': return '\a';
            case '0': case '1': case '2': case '3':
            case '4': case '5': case '6': case '7':
            {
                int value = e - '0';
                for (int k = 0; k < 2 && i < end && s[i] is >= '0' and <= '7'; k++)
                    value = value * 8 + (s[i++] - '0');
                return value;
            }
            case 'x':
            {
                int value = 0;
                bool any = false;
                while (i < end && Uri.IsHexDigit(s[i]))
                {
                    value = value * 16 + Convert.ToInt32(s[i].ToString(), 16);
                    i++;
                    any = true;
                }
                if (!any) error ??= "\\x used with no following hex digits";
                return value;
            }
            case '\\': return '\\';
            case '\'': return '\'';
            case '"': return '"';
            case '?': return '?';
            default:
                error ??= $"unknown escape sequence '\\{e}'";
                return e;
        }
    }
}
