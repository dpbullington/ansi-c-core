using System.Text.RegularExpressions;

namespace AnsiCCore.Tests.Emit;

/// <summary>
/// The "final boss" harness: each complete C program in <c>Programs/</c> is transpiled, compiled,
/// and run, and its stdout + exit code are checked against committed golden references
/// (<c>name.c.out</c>, and an optional <c>// exit: N</c> header). The emitted C# is also snapshotted
/// to <c>name.c.cs</c>. Regenerate every golden with <c>UPDATE_GOLDENS=1 dotnet test</c>.
/// </summary>
[TestFixture]
public class GoldenProgramTests
{
    private static bool Update => Environment.GetEnvironmentVariable("UPDATE_GOLDENS") == "1";

    /// <summary>Locates the source <c>Programs/</c> directory by walking up to the test project.</summary>
    private static string ProgramsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AnsiCCore.Tests.csproj")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("could not locate AnsiCCore.Tests project directory");
        return Path.Combine(dir.FullName, "Programs");
    }

    public static IEnumerable<string> ProgramNames()
        => Directory.EnumerateFiles(ProgramsDir(), "*.c").Select(Path.GetFileName)!.OrderBy(n => n, StringComparer.Ordinal)!;

    [TestCaseSource(nameof(ProgramNames))]
    public void CompilesRunsAndMatchesReference(string name)
    {
        string path = Path.Combine(ProgramsDir(), name);
        string source = File.ReadAllText(path);
        int expectedExit = ParseExpectedExit(source);

        var (exit, output) = EmitTestHost.RunWithOutput(source);

        string outPath = path + ".out";
        if (Update)
            File.WriteAllText(outPath, output);

        string expectedOutput = File.Exists(outPath) ? File.ReadAllText(outPath) : string.Empty;
        Assert.Multiple(() =>
        {
            Assert.That(Normalize(output), Is.EqualTo(Normalize(expectedOutput)), "stdout mismatch");
            Assert.That(exit, Is.EqualTo(expectedExit), "exit code mismatch");
        });
    }

    [TestCaseSource(nameof(ProgramNames))]
    public void EmittedCSharpMatchesSnapshot(string name)
    {
        string path = Path.Combine(ProgramsDir(), name);
        string csharp = EmitTestHost.Transpile(File.ReadAllText(path));

        string snapshotPath = path + ".cs";
        if (Update)
        {
            File.WriteAllText(snapshotPath, csharp);
            Assert.Pass("snapshot updated");
        }

        Assert.That(File.Exists(snapshotPath), Is.True, $"missing snapshot {snapshotPath}; run with UPDATE_GOLDENS=1");
        Assert.That(Normalize(csharp), Is.EqualTo(Normalize(File.ReadAllText(snapshotPath))));
    }

    private static int ParseExpectedExit(string source)
    {
        var match = Regex.Match(source, @"//\s*exit:\s*(-?\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    /// <summary>Normalizes line endings so goldens are stable across platforms.</summary>
    private static string Normalize(string s) => s.Replace("\r\n", "\n");
}
