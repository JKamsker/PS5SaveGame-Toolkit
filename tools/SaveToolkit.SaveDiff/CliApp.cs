using SaveToolkit.Core.Storage;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.SaveDiff;

/// <summary>Entry point logic, separated from <c>Main</c> so tests can drive it with their own writers.</summary>
internal static class CliApp
{
    public const int ExitSame = 0;
    public const int ExitDifferent = 1;
    public const int ExitError = 2;

    public const string Usage = """
        savediff — read-only differential save tool (PS5 Save Game Toolkit)

          savediff list    <save>                    Directories, generations, backup twins and hashes
          savediff compare <before> <after> [opts]   Byte (and optional semantic) diff of two save sets
          savediff verify  <manifest.json> [opts]    Check fixture expectations against the artifacts
          savediff list-decoders [opts]             Built-in modes, plugin views and decoder hooks

        <save> is a decrypted dump .zip, a folder or a single file. Repeated ZIP entries become
        generations 0, 1, ... in archive order; use the same zip twice with --before-gen/--after-gen
        to diff two snapshots of one dump.

        compare options:
          --before-gen, --after-gen, --gen  all|latest|N   generations to compare (default all)
          --file GLOB          only matching files (repeatable; path in save dir or rawdir/path)
          --title PPSAxxxxx    only this title
          --ignore FILE        churn rules from a manifest / ignore file (repeatable)
          --semantic MODE      none|json|auto|<plugin-id> (default none = bytes only)
          --decoder ID         select a listed mode, plugin view or hook (exclusive with --semantic)
          --max-query-rows N   F8 rows per file (default 100000)
          --max-query-pages N  F8 pages per file (default 1000)
          --max-query-bytes N  UTF-8 F8 response bytes per file (default 33554432)
          --system             include sce_sys/* files
          --no-collapse        diff backups even when they mirror their main file
          --all                also list unchanged files
          --max-ranges N  --merge-gap N  --context N  --preview N  --max-changes N
        verify options:
          --artifacts DIR      where archives live (default: SAVETOOLKIT_ARTIFACTS, ./artifacts, manifest dir)
          --fixture ID         only this fixture (repeatable)
        common:
          --format text|json   --out FILE (refused inside artifacts/ or a git working tree)

        Exit codes: 0 no changes / all fixtures pass, 1 changes / a fixture failed, 2 usage or input error.
        """;

    private static readonly HashSet<string> CompareFlags = ["system", "no-collapse", "all"];
    private static readonly HashSet<string> CompareValued =
        ["before-gen", "after-gen", "gen", "file", "title", "ignore", "semantic", "decoder", "max-query-rows", "max-query-pages", "max-query-bytes", "max-ranges", "merge-gap", "context", "preview", "max-changes", "format", "out"];
    private static readonly HashSet<string> VerifyValued = ["artifacts", "fixture", "format", "out"];
    private static readonly HashSet<string> ListValued = ["format", "out"];
    private static readonly HashSet<string> NoFlags = [];

    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr, Func<DecoderResolver>? resolver = null)
    {
        resolver ??= DecoderResolver.Discover;
        try
        {
            if (args.Count == 0 || args[0] is "help" or "--help" or "-h")
            {
                Emit(null, stdout, Usage);
                return args.Count == 0 ? ExitError : ExitSame;
            }

            if (args.Count == 2 && args[0] is "compare" or "verify" or "list" or "list-decoders"
                && args[1] is "--help" or "-h")
            {
                Emit(null, stdout, Usage);
                return ExitSame;
            }

            return args[0] switch
            {
                "compare" => CompareCommand.Run(CliArguments.Parse(args, CompareFlags, CompareValued), stdout, resolver),
                "verify" => VerifyCommand.Run(CliArguments.Parse(args, NoFlags, VerifyValued), stdout, resolver),
                "list" => ListCommand.Run(CliArguments.Parse(args, NoFlags, ListValued), stdout),
                "list-decoders" => ListDecodersCommand.Run(CliArguments.Parse(args, NoFlags, ListValued), stdout, resolver),
                _ => throw new UsageException($"Unknown command '{args[0]}'. Run 'savediff help'."),
            };
        }
        catch (Exception ex) when (ex is UsageException or IOException or UnauthorizedAccessException
                                       or InvalidDataException or ImportException or FixtureManifestException or ArgumentException)
        {
            stderr.WriteLine($"savediff: {ex.Message}");
            return ExitError;
        }
    }

    /// <summary>Validates <c>--out</c> before any work is done; returns the full path, or null for stdout.</summary>
    public static string? OutputPath(CliArguments args, params IEnumerable<string> inputs) =>
        args.Get("out") is { } path ? OutputGuard.Check(path, inputs) : null;

    /// <summary>Writes the rendered report to the checked <paramref name="outputPath"/> or stdout.</summary>
    public static void Emit(string? outputPath, TextWriter stdout, string text)
    {
        // Renderers and indented JSON can use host newlines. Keep report bytes portable.
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (outputPath is { } path)
        {
            File.WriteAllText(path, text);
            stdout.WriteLine($"Report written to {path}");
        }
        else
        {
            stdout.Write(text);
        }
    }

    /// <summary>Use forward slashes for host path separators in report metadata.</summary>
    public static string DisplayPath(string path) => path.Replace(Path.DirectorySeparatorChar, '/');

    public static bool WantsJson(CliArguments args) => args.Get("format") switch
    {
        null or "text" => false,
        "json" => true,
        var other => throw new UsageException($"--format must be text or json, not '{other}'."),
    };

    public static GenerationSelector Generation(CliArguments args, string name) =>
        GenerationSelector.TryParse(args.Get(name) ?? args.Get("gen"), out var selector)
            ? selector
            : throw new UsageException($"--{name} must be all, latest or a number.");
}
