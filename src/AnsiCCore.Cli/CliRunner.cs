using AnsiCCore.Driver;
using AnsiCCore.Frontend.Diagnostics;
using AnsiCCore.Frontend.Lexing;

namespace AnsiCCore.Cli;

/// <summary>
/// Parses command-line arguments and drives a transpiler run. Written to take explicit
/// output writers and return an exit code so it is testable without touching the process.
/// </summary>
public static class CliRunner
{
    // Exit codes modeled on sysexits.h.
    private const int ExitOk = 0;
    private const int ExitError = 1;      // diagnostics with errors
    private const int ExitUsage = 64;     // EX_USAGE

    public static int Run(string[] args, TextWriter outWriter, TextWriter errWriter)
    {
        if (!TryParseArgs(args, errWriter, out var inputs, out var options, out bool dumpTokens, out bool dumpAst, out bool showHelp))
        {
            if (showHelp) { PrintUsage(outWriter); return ExitOk; }
            return ExitUsage;
        }

        if (inputs.Count == 0)
        {
            errWriter.WriteLine("ansicc: no input files");
            PrintUsage(errWriter);
            return ExitUsage;
        }

        var driver = new TranspilerDriver(options);
        var result = driver.Run(inputs);

        foreach (var unit in result.Units)
        {
            foreach (var diag in unit.Diagnostics)
                errWriter.WriteLine(diag.ToString());

            if (dumpTokens)
                DumpTokens(outWriter, unit);

            if (dumpAst && unit.Ast != null)
            {
                outWriter.WriteLine($"// AST for {unit.Path}");
                outWriter.Write(AstPrinter.Print(unit.Ast));
            }

            if (!dumpTokens && !dumpAst)
            {
                int items = unit.Ast?.Items.Count ?? 0;
                int errors = unit.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
                outWriter.WriteLine($"{unit.Path}: {items} top-level item(s), {errors} error(s)");
            }
        }

        if (options.OutputDirectory != null && result.Success)
            outWriter.WriteLine("ansicc: parsing succeeded; code generation is not yet implemented, no output written");

        return result.Success ? ExitOk : ExitError;
    }

    private static void DumpTokens(TextWriter outWriter, TranslationUnitResult unit)
    {
        outWriter.WriteLine($"// tokens for {unit.Path}");
        foreach (var t in unit.Tokens)
        {
            if (t.Kind == TokenKind.EndOfFile) continue;
            outWriter.WriteLine($"  {t.Location.Line,4}:{t.Location.Column,-3} {t.Kind,-20} {t.Text}");
        }
    }

    private static bool TryParseArgs(
        string[] args, TextWriter errWriter,
        out List<string> inputs, out DriverOptions options,
        out bool dumpTokens, out bool dumpAst, out bool showHelp)
    {
        inputs = [];
        options = DriverOptions.Default;
        dumpTokens = false;
        dumpAst = false;
        showHelp = false;

        var includeDirs = new List<string>();
        var macros = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? outputDir = null;
        bool allowLineComments = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    showHelp = true;
                    return false; // caller prints usage and exits OK
                case "--dump-tokens":
                    dumpTokens = true;
                    break;
                case "--dump-ast":
                    dumpAst = true;
                    break;
                case "--allow-line-comments":
                    allowLineComments = true;
                    break;
                case "-o":
                    if (!TryTakeValue(args, ref i, errWriter, "-o")) return false;
                    outputDir = args[i];
                    break;
                case "-I":
                    if (!TryTakeValue(args, ref i, errWriter, "-I")) return false;
                    includeDirs.Add(args[i]);
                    break;
                case "-D":
                    if (!TryTakeValue(args, ref i, errWriter, "-D")) return false;
                    AddMacro(macros, args[i]);
                    break;
                default:
                    if (arg.StartsWith("-I", StringComparison.Ordinal) && arg.Length > 2)
                        includeDirs.Add(arg[2..]);
                    else if (arg.StartsWith("-D", StringComparison.Ordinal) && arg.Length > 2)
                        AddMacro(macros, arg[2..]);
                    else if (arg.StartsWith('-') && arg.Length > 1)
                    {
                        errWriter.WriteLine($"ansicc: unknown option '{arg}'");
                        return false;
                    }
                    else
                        inputs.Add(arg);
                    break;
            }
        }

        options = new DriverOptions
        {
            IncludeDirectories = includeDirs,
            PredefinedMacros = macros,
            OutputDirectory = outputDir,
            AllowLineComments = allowLineComments,
        };
        return true;
    }

    private static bool TryTakeValue(string[] args, ref int i, TextWriter errWriter, string option)
    {
        if (i + 1 >= args.Length)
        {
            errWriter.WriteLine($"ansicc: option '{option}' requires an argument");
            return false;
        }
        i++;
        return true;
    }

    private static void AddMacro(Dictionary<string, string?> macros, string spec)
    {
        int eq = spec.IndexOf('=');
        if (eq < 0) macros[spec] = null;
        else macros[spec[..eq]] = spec[(eq + 1)..];
    }

    private static void PrintUsage(TextWriter w)
    {
        w.WriteLine("usage: ansicc [options] <input.c ...>");
        w.WriteLine();
        w.WriteLine("options:");
        w.WriteLine("  -o <dir>               output directory (code generation not yet implemented)");
        w.WriteLine("  -I <dir>               add a directory to the #include search path (repeatable)");
        w.WriteLine("  -D <name>[=value]      predefine a macro (repeatable; value defaults to 1)");
        w.WriteLine("  --dump-tokens          print the preprocessed token stream");
        w.WriteLine("  --dump-ast             print an outline of the parse tree");
        w.WriteLine("  --allow-line-comments  accept // line comments (relaxes strict C89)");
        w.WriteLine("  -h, --help             show this help");
    }
}
