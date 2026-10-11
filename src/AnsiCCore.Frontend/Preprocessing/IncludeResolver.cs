namespace AnsiCCore.Frontend.Preprocessing;

/// <summary>A successfully resolved <c>#include</c>: the canonical path and the file text.</summary>
public readonly record struct ResolvedInclude(string FullPath, string Content);

/// <summary>
/// Resolves <c>#include</c> directives to source text. Abstracted so the preprocessor never
/// touches the file system directly — tests use an in-memory resolver; the CLI uses the
/// file-system resolver.
/// </summary>
public interface IIncludeResolver
{
    /// <param name="path">The requested header name, e.g. <c>stdio.h</c> or <c>sys/types.h</c>.</param>
    /// <param name="angled">True for <c>&lt;...&gt;</c>, false for <c>"..."</c>.</param>
    /// <param name="includingFileDirectory">Directory of the file issuing the include (for quote includes).</param>
    /// <param name="result">The resolved file when the method returns true.</param>
    /// <returns>True if the header was found.</returns>
    bool TryResolve(string path, bool angled, string includingFileDirectory, out ResolvedInclude result);
}

/// <summary>In-memory resolver backed by a name→content map. Primarily for tests.</summary>
public sealed class InMemoryIncludeResolver : IIncludeResolver
{
    private readonly Dictionary<string, string> _files;

    public InMemoryIncludeResolver(IDictionary<string, string> files)
        => _files = new Dictionary<string, string>(files, StringComparer.Ordinal);

    public bool TryResolve(string path, bool angled, string includingFileDirectory, out ResolvedInclude result)
    {
        if (_files.TryGetValue(path, out var content))
        {
            result = new ResolvedInclude(path, content);
            return true;
        }
        result = default;
        return false;
    }
}

/// <summary>
/// Resolves includes against the real file system. Angle includes search the configured
/// system directories in order; quote includes search the including file's directory first,
/// then the quote directories, then the system directories (the common search order).
/// </summary>
public sealed class FileSystemIncludeResolver : IIncludeResolver
{
    private readonly IReadOnlyList<string> _quoteDirs;
    private readonly IReadOnlyList<string> _angleDirs;

    public FileSystemIncludeResolver(IEnumerable<string>? quoteDirs = null, IEnumerable<string>? angleDirs = null)
    {
        _quoteDirs = quoteDirs?.ToArray() ?? [];
        _angleDirs = angleDirs?.ToArray() ?? [];
    }

    public bool TryResolve(string path, bool angled, string includingFileDirectory, out ResolvedInclude result)
    {
        IEnumerable<string> searchDirs = angled
            ? _angleDirs
            : new[] { includingFileDirectory }.Concat(_quoteDirs).Concat(_angleDirs);

        foreach (var dir in searchDirs)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            string candidate = Path.Combine(dir, path);
            if (File.Exists(candidate))
            {
                try
                {
                    result = new ResolvedInclude(Path.GetFullPath(candidate), File.ReadAllText(candidate));
                    return true;
                }
                catch (IOException)
                {
                    // Keep searching other directories on a read failure.
                }
            }
        }

        result = default;
        return false;
    }
}
