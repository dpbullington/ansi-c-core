// AnsiCCore CLI — thin wrapper over AnsiCCore.Driver.
// Usage (planned): ansicc <input.c ...> [-o <out-dir>]
//
// This is a placeholder entry point so the solution builds and runs end-to-end
// while the transpiler pipeline is implemented. See CLAUDE.md for architecture.

if (args.Length == 0)
{
    Console.Error.WriteLine("ansicc: no input files");
    Console.Error.WriteLine("usage: ansicc <input.c ...> [-o <out-dir>]");
    return 64; // EX_USAGE
}

Console.WriteLine($"ansicc: pipeline not yet implemented; received {args.Length} argument(s)");
return 0;
