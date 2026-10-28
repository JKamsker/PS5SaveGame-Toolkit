using SaveToolkit.Testing.Diff;

namespace SaveToolkit.SaveDiff;

internal static class CompareCommand
{
    public static int Run(CliArguments args, TextWriter stdout, Func<DecoderResolver> resolver)
    {
        args.ExpectPositionals(2, "savediff compare <before> <after> [options]");
        var json = CliApp.WantsJson(args);
        var options = BuildOptions(args, resolver);

        // The same path on both sides (two generations of one dump) is imported once.
        var samePath = string.Equals(Path.GetFullPath(args.Positionals[0]), Path.GetFullPath(args.Positionals[1]), StringComparison.OrdinalIgnoreCase);
        using var before = SaveSetLoader.Load(args.Positionals[0]);
        using var afterOwned = samePath ? null : SaveSetLoader.Load(args.Positionals[1]);
        var after = afterOwned ?? before;

        var report = SaveSetDiffer.Compare(before.Set, after.Set, options, args.Positionals[0], args.Positionals[1]);
        CliApp.Emit(args, stdout, json ? DiffJson.Serialize(report) + "\n" : TextReportWriter.Render(report, args.Has("all")));
        return report.HasChanges ? CliApp.ExitDifferent : CliApp.ExitSame;
    }

    internal static SaveDiffOptions BuildOptions(CliArguments args, Func<DecoderResolver> resolver)
    {
        var rules = args.GetAll("ignore").SelectMany(path => FixtureManifest.Load(path).Ignore);
        var defaults = new ByteDiffOptions();
        var semantic = args.Get("semantic");
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
            Decoder = semantic is null or "none" ? null : resolver().Resolve(semantic),
        };
    }
}
