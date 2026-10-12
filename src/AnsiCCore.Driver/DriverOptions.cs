namespace AnsiCCore.Driver;

/// <summary>Options controlling a transpiler run across one or more translation units.</summary>
public sealed record DriverOptions
{
    /// <summary>Directories searched for <c>#include</c> headers (both quote and angle forms).</summary>
    public IReadOnlyList<string> IncludeDirectories { get; init; } = [];

    /// <summary>Command-line macro definitions (<c>-D</c>); a null/empty value means <c>1</c>.</summary>
    public IReadOnlyDictionary<string, string?> PredefinedMacros { get; init; }
        = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>Where emitted C# will be written once Emit exists. Unused for now.</summary>
    public string? OutputDirectory { get; init; }

    /// <summary>Relax strict C89 to accept <c>//</c> line comments.</summary>
    public bool AllowLineComments { get; init; }

    public static DriverOptions Default { get; } = new();
}
