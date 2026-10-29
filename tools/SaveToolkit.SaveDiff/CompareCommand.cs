using SaveToolkit.Testing.Diff;

namespace SaveToolkit.SaveDiff;

internal static class CompareCommand
{
    public static int Run(CliArguments args, TextWriter stdout, Func<DecoderResolver> resolver)
    {
        args.ExpectPositionals(2, "savediff compare <before> <after> [options]");
        var json = CliApp.WantsJson(args);
        var output = CliApp.OutputPath(args, [.. args.Positionals, .. args.GetAll("ignore")]);
        var options = BuildOptions(args, resolver);

        // The same path on both sides (two generations of one dump) is imported once.
        // Reuse only exact paths: case-only differences can identify different files on the host.
        var samePath = string.Equals(Path.GetFullPath(args.Positionals[0]), Path.GetFullPath(args.Positionals[1]), StringComparison.Ordinal);
        using var before = SaveSetLoader.Load(args.Positionals[0]);
        using var afterOwned = samePath ? null : SaveSetLoader.Load(args.Positionals[1]);
        var after = afterOwned ?? before;

        var report = SaveSetDiffer.Compare(before.Set, after.Set, options, CliApp.DisplayPath(args.Positionals[0]), CliApp.DisplayPath(args.Positionals[1]));
        CliApp.Emit(output, stdout, json ? DiffJson.Serialize(report) + "\n" : TextReportWriter.Render(report, args.Has("all")));
        return report.HasChanges ? CliApp.ExitDifferent : CliApp.ExitSame;
    }

    internal static SaveDiffOptions BuildOptions(CliArguments args, Func<DecoderResolver> resolver)
    {
        var rules = args.GetAll("ignore").SelectMany(path => FixtureManifest.Load(path).Ignore);
        var defaults = new ByteDiffOptions();
        if (args.Get("semantic") is not null && args.Get("decoder") is not null)
        {
            throw new UsageException("Use either --decoder or --semantic, not both.");
        }

        var semantic = args.Get("decoder") ?? args.Get("semantic");
        var decodeLimits = new DecodeLimits();
        return new SaveDiffOptions
        {
            BeforeGeneration = CliApp.Generation(args, "before-gen"),
            AfterGeneration = CliApp.Generation(args, "after-gen"),
            IncludeSystemFiles = args.Has("system"),
            CollapseMirroredBackups = !args.Has("no-collapse"),
            FileFilters = args.GetAll("file"),
            TitleFilter = args.Get("title"),
            Churn = new ChurnFilter(rules),
            Bytes = new ByteDiffOptions
            {
                MaxRanges = args.GetInt("max-ranges", defaults.MaxRanges),
                MergeGap = args.GetInt("merge-gap", defaults.MergeGap),
                ContextBytes = args.GetInt("context", defaults.ContextBytes),
                MaxPreviewBytes = args.GetInt("preview", defaults.MaxPreviewBytes, min: 1),
            },
            MaxSemanticChanges = args.GetInt("max-changes", 500),
            DecodeLimits = decodeLimits with
            {
                MaxQueryRows = args.GetInt("max-query-rows", decodeLimits.MaxQueryRows),
                MaxQueryPages = args.GetInt("max-query-pages", decodeLimits.MaxQueryPages),
                MaxQueryBytes = args.GetInt("max-query-bytes", checked((int)decodeLimits.MaxQueryBytes)),
            },
            Decoder = semantic is null or "none" ? null : resolver().Resolve(semantic),
        };
    }
}
