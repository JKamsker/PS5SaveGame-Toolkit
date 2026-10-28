using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Testing.Diff;

/// <summary>
/// Read-only differential comparison of two save sets (two dumps, or two generations of one dump).
/// Files are paired by title / save directory / backup flag / path and generation rank, never by
/// archive order. Usable from plugin tests:
/// <code>
/// var report = SaveSetDiffer.Compare(SampleSaves.Load("PPSA01325"), SampleSaves.Load("PPSA01325"),
///     new SaveDiffOptions { BeforeGeneration = GenerationSelector.Only(0), AfterGeneration = GenerationSelector.Only(1) });
/// </code>
/// </summary>
public static class SaveSetDiffer
{
    public static SaveDiffReport Compare(ISaveSet before, ISaveSet after, SaveDiffOptions? options = null, string beforeLabel = "before", string afterLabel = "after") =>
        ComparePairs(before, after, options ?? new SaveDiffOptions(), beforeLabel, afterLabel).Report;

    internal static (SaveDiffReport Report, IReadOnlyList<(PairedFile Pair, FileDiff Diff)> Files) ComparePairs(
        ISaveSet before, ISaveSet after, SaveDiffOptions options, string beforeLabel, string afterLabel)
    {
        var fingerprints = new Fingerprints();
        var beforeEntries = SaveSetPairing.Entries(before, options, options.BeforeGeneration);
        var afterEntries = SaveSetPairing.Entries(after, options, options.AfterGeneration);
        var pairs = SaveSetPairing.Pair(beforeEntries, afterEntries);

        var diffs = new Dictionary<PairedFile, FileDiff>(ReferenceEqualityComparer.Instance);
        foreach (var pair in pairs.Where(p => !p.Any.IsBackup))
        {
            diffs[pair] = Diff(pair, options, fingerprints, mirror: null);
        }

        var mains = pairs.Where(p => !p.Any.IsBackup).ToDictionary(p => (p.Any.PathKey, p.Any.Rank));
        foreach (var pair in pairs.Where(p => p.Any.IsBackup))
        {
            var e = pair.Any;
            var main = options.CollapseMirroredBackups ? mains.GetValueOrDefault((SetEntry.MakeKey(e.TitleId, e.Directory, false, e.Path), e.Rank)) : null;
            diffs[pair] = Diff(pair, options, fingerprints, main);
        }

        var files = pairs.Select(p => (p, diffs[p])).ToList();
        var list = files.Select(f => f.Item2).ToList();
        var report = new SaveDiffReport
        {
            Before = Summarize(beforeLabel, options.BeforeGeneration, beforeEntries, fingerprints),
            After = Summarize(afterLabel, options.AfterGeneration, afterEntries, fingerprints),
            Totals = Totals(list),
            Files = list,
            IgnoreRules = [.. options.Churn.Rules.Select(r => r.ToString())],
        };
        return (report, files);
    }

    private static FileDiff Diff(PairedFile pair, SaveDiffOptions options, Fingerprints fingerprints, PairedFile? mirror)
    {
        var (b, a) = (pair.Before, pair.After);
        var e = pair.Any;
        var diff = new FileDiff
        {
            TitleId = e.TitleId,
            Directory = e.Directory,
            Path = e.Path,
            IsBackup = e.IsBackup,
            BeforeGeneration = b?.Generation,
            AfterGeneration = a?.Generation,
            BeforeLength = b?.File.Length,
            AfterLength = a?.File.Length,
            BeforeSha256 = b is null ? null : fingerprints.Sha256(b.File),
            AfterSha256 = a is null ? null : fingerprints.Sha256(a.File),
        };

        if (b is null || a is null)
        {
            return diff with { Status = b is null ? FileDiffStatus.Added : FileDiffStatus.Removed };
        }

        if (diff.BeforeSha256 == diff.AfterSha256)
        {
            return diff with { Status = FileDiffStatus.Unchanged };
        }

        if (mirror is { Before: { } mb, After: { } ma }
            && fingerprints.Sha256(mb.File) == diff.BeforeSha256
            && fingerprints.Sha256(ma.File) == diff.AfterSha256)
        {
            return diff with { Status = FileDiffStatus.MirrorsMain, Note = "Identical to the main file on both sides; see its diff." };
        }

        var churn = options.Churn.ForFile(e.TitleId, e.Directory, e.Path);
        if (churn.WholeFileRule is { } rule)
        {
            return diff with { Status = FileDiffStatus.ChurnOnly, Note = $"Ignored: {rule.Reason}" };
        }

        var bytes = ByteDiffer.Compare(b.File.OpenRead, a.File.OpenRead, churn.ByteIntervals, options.Bytes);
        var semantic = options.Decoder is { } decoder ? Semantic(decoder, b.File, a.File, churn, options) : null;

        // Masked bytes must not hide a value change the decoder still sees.
        var changed = bytes.HasChanges || semantic is { Total: > 0 };
        return diff with
        {
            Status = changed ? FileDiffStatus.Changed : FileDiffStatus.ChurnOnly,
            Bytes = bytes,
            Semantic = semantic,
        };
    }

    private static SemanticDiffResult? Semantic(ISaveDecoder decoder, ISaveFile before, ISaveFile after, FileChurn churn, SaveDiffOptions options)
    {
        if (!decoder.CanDecode(before) || !decoder.CanDecode(after))
        {
            return null;
        }

        try
        {
            var b = decoder.Decode(before, options.DecodeLimits);
            var a = decoder.Decode(after, options.DecodeLimits);
            return SemanticDiffer.Compare(b, a, churn, options.MaxSemanticChanges);
        }
        catch (Exception ex)
        {
            // A decoder failure must not abort the byte report; it is shown per file instead.
            return SemanticDiffResult.Failed(decoder.Id, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static SaveSetSummary Summarize(string label, GenerationSelector selector, IReadOnlyList<SetEntry> entries, Fingerprints fingerprints) => new()
    {
        Label = label,
        Generation = selector.ToString(),
        Directories = entries.Select(e => (e.File.Directory.Id)).Distinct(StringComparer.Ordinal).Count(),
        Files = entries.Count,
        Generations = entries.Count == 0 ? 0 : entries.Select(e => e.Generation).Distinct().Count(),
        TitleIds = [.. entries.Select(e => e.TitleId).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)],
        Twins = SaveSetPairing.Twins(entries, fingerprints),
    };

    private static DiffTotals Totals(IReadOnlyList<FileDiff> files) => new()
    {
        Files = files.Count,
        Changed = files.Count(f => f.Status == FileDiffStatus.Changed),
        ChurnOnly = files.Count(f => f.Status == FileDiffStatus.ChurnOnly),
        Added = files.Count(f => f.Status == FileDiffStatus.Added),
        Removed = files.Count(f => f.Status == FileDiffStatus.Removed),
        Unchanged = files.Count(f => f.Status == FileDiffStatus.Unchanged),
        MirrorsMain = files.Count(f => f.Status == FileDiffStatus.MirrorsMain),
        ChangedBytes = files.Sum(f => f.Bytes?.ChangedBytes ?? 0),
        IgnoredBytes = files.Sum(f => f.Bytes?.IgnoredBytes ?? 0),
        SemanticChanges = files.Sum(f => f.Semantic?.Total ?? 0),
        SemanticIgnored = files.Sum(f => f.Semantic?.Ignored ?? 0),
        SemanticErrors = files.Count(f => f.Semantic?.Error is not null),
    };
}
