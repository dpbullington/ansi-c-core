namespace AnsiCCore.Frontend.Diagnostics;

/// <summary>
/// A position in a source file. Lines and columns are 1-based; <see cref="Offset"/> is a
/// 0-based index into the source text. A column counts UTF-16 code units (good enough for
/// ANSI C source, which is expected to be in the source character set).
/// </summary>
public readonly record struct SourceLocation(string FileName, int Line, int Column, int Offset)
{
    public override string ToString() => $"{FileName}:{Line}:{Column}";
}
