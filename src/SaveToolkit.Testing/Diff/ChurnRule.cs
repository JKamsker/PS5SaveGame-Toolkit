namespace SaveToolkit.Testing.Diff;

/// <summary>
/// One explained source of noise (save timestamp, play-time counter, relocation table, ...) that a
/// comparison should not report as a change. A rule masks either a byte range (<see cref="Offset"/> +
/// <see cref="Length"/>), semantic paths (<see cref="Path"/> glob) or, with neither, the whole file.
/// Masked differences are counted in the report, never silently dropped.
/// </summary>
public sealed record ChurnRule
{
    /// <summary>Title id the rule is for; null or <c>*</c> applies to every title.</summary>
    public string? TitleId { get; init; }

    /// <summary>Glob over the logical save directory name (without <c>sdimg_</c>/<c>sdimg_sce_bu_</c>).</summary>
    public string Directory { get; init; } = "*";

    /// <summary>Glob over the path inside the save directory, e.g. <c>*.save</c> or <c>sce_sys/param.sfo</c>.</summary>
    public string File { get; init; } = "*";

    public long? Offset { get; init; }

    public long? Length { get; init; }

    /// <summary>Glob over semantic paths produced by a decoder, e.g. <c>Profile/LastSave*</c>.</summary>
    public string? Path { get; init; }

    /// <summary>Why this is noise. Required: only explained churn may be ignored.</summary>
    public string Reason { get; init; } = "";

    public bool IsWholeFile => Offset is null && Path is null;

    public bool AppliesTo(string titleId, string directory, string pathInDirectory) =>
        (TitleId is null or "*" || string.Equals(TitleId, titleId, StringComparison.OrdinalIgnoreCase))
        && Glob.IsMatch(Directory, directory)
        && Glob.IsMatch(File, pathInDirectory);

    /// <summary>Problems with this rule, empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Reason))
        {
            errors.Add("every ignore rule needs a 'reason'");
        }

        if (Offset is null != Length is null)
        {
            errors.Add("'offset' and 'length' must be given together");
        }

        if (Offset < 0 || Length <= 0)
        {
            errors.Add("'offset' must be >= 0 and 'length' > 0");
        }

        if (Offset is not null && Path is not null)
        {
            errors.Add("a rule masks either a byte range or a semantic path, not both");
        }

        return errors;
    }

    public override string ToString()
    {
        var target = Offset is { } o ? $"bytes 0x{o:X}+{Length}" : Path is { } p ? $"path {p}" : "whole file";
        return $"{TitleId ?? "*"}/{Directory}/{File} {target}: {Reason}";
    }
}

/// <summary>Half-open byte interval [<see cref="Start"/>, <see cref="End"/>).</summary>
public readonly record struct ByteInterval(long Start, long End)
{
    public long Length => End - Start;

    public bool Overlaps(long start, long end) => Start < end && start < End;
}

/// <summary>The churn rules that apply to one file.</summary>
public sealed class FileChurn
{
    public static FileChurn None { get; } = new([]);

    internal FileChurn(IReadOnlyList<ChurnRule> rules)
    {
        Rules = rules;
        WholeFileRule = rules.FirstOrDefault(r => r.IsWholeFile);
        ByteIntervals = Merge(rules.Where(r => r.Offset is not null).Select(r => new ByteInterval(r.Offset!.Value, r.Offset.Value + r.Length!.Value)));
    }

    public IReadOnlyList<ChurnRule> Rules { get; }

    public ChurnRule? WholeFileRule { get; }

    /// <summary>Sorted, non-overlapping masked byte ranges.</summary>
    public IReadOnlyList<ByteInterval> ByteIntervals { get; }

    public ChurnRule? MatchPath(string semanticPath) =>
        Rules.FirstOrDefault(r => r.Path is { } glob && Glob.IsMatch(glob, semanticPath));

    /// <summary>Sorts and merges overlapping or touching intervals.</summary>
    internal static List<ByteInterval> Merge(IEnumerable<ByteInterval> intervals)
    {
        var merged = new List<ByteInterval>();
        foreach (var next in intervals.OrderBy(i => i.Start))
        {
            if (merged.Count > 0 && next.Start <= merged[^1].End)
            {
                merged[^1] = merged[^1] with { End = Math.Max(merged[^1].End, next.End) };
            }
            else
            {
                merged.Add(next);
            }
        }

        return merged;
    }
}

/// <summary>A validated set of churn rules (from <c>--ignore</c> files or a manifest's <c>ignore</c> block).</summary>
public sealed class ChurnFilter
{
    public static ChurnFilter None { get; } = new([]);

    public ChurnFilter(IEnumerable<ChurnRule> rules)
    {
        Rules = [.. rules];
        var errors = Rules.SelectMany((r, i) => r.Validate().Select(e => $"ignore rule {i} ({r.File}): {e}")).ToList();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(rules));
        }
    }

    public IReadOnlyList<ChurnRule> Rules { get; }

    public FileChurn ForFile(string titleId, string directory, string pathInDirectory)
    {
        var rules = Rules.Where(r => r.AppliesTo(titleId, directory, pathInDirectory)).ToList();
        return rules.Count == 0 ? FileChurn.None : new FileChurn(rules);
    }

    public ChurnFilter With(IEnumerable<ChurnRule> more) => new(Rules.Concat(more));
}
