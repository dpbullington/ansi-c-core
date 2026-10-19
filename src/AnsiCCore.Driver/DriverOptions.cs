namespace AnsiCCore.Driver;

/// <summary>Options controlling a transpiler run across one or more translation units.</summary>
public sealed record DriverOptions
{
    /// <summary>Directories searched for <c>#include</c> headers (both quote and angle forms).</summary>
    public IReadOnlyList<string> IncludeDirectories { get; init; } = [];

    /// <summary>Command-line macro definitions (<c>-D</c>); a null/empty value means <c>1</c>.</summary>
    public IReadOnlyDictionary<string, string?> PredefinedMacros { get; init; }
        = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>Paths to .NET assemblies to "link" (<c>--link</c>): their <c>[CExport]</c> methods become resolvable C functions.</summary>
    public IReadOnlyList<string> LinkedAssemblies { get; init; } = [];

    /// <summary>Directory to write emitted C# to; null means don't write files.</summary>
    public string? OutputDirectory { get; init; }

    /// <summary>Run the Emit stage. Off by default so analysis-only runs skip code generation.</summary>
    public bool EmitCSharp { get; init; }

    /// <summary>Relax strict C89 to accept <c>//</c> line comments.</summary>
    public bool AllowLineComments { get; init; }

    public static DriverOptions Default { get; } = new();
}
