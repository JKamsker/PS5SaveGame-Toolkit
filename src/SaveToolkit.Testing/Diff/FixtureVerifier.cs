using System.Security.Cryptography;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Testing.Diff;

public sealed record ExpectationResult(ExpectedChange Expectation, bool Passed, string Message);

public sealed record FixtureVerification
{
    public required string FixtureId { get; init; }

    public required SaveDiffReport Report { get; init; }

    public IReadOnlyList<ExpectationResult> Expectations { get; init; } = [];

    /// <summary>Strict mode: unmasked changes no expectation accounts for.</summary>
    public IReadOnlyList<string> Unexpected { get; init; } = [];

    public IReadOnlyList<string> Errors { get; init; } = [];

    public bool Passed => Errors.Count == 0 && Unexpected.Count == 0 && Expectations.All(e => e.Passed);
}

/// <summary>
/// Checks a <see cref="Fixture"/>'s expectations against two loaded save sets. Byte expectations read the
/// files directly (independent of report caps); semantic expectations decode both files.
/// </summary>
public static class FixtureVerifier
{
    /// <summary>Comparison options for a fixture: its generations, file filters and combined churn rules.</summary>
    public static SaveDiffOptions OptionsFor(Fixture fixture, FixtureManifest manifest, ISaveDecoder? decoder, SaveDiffOptions? baseOptions = null) =>
        (baseOptions ?? new SaveDiffOptions()) with
        {
            BeforeGeneration = fixture.Before?.Generation ?? GenerationSelector.All,
            AfterGeneration = fixture.After?.Generation ?? GenerationSelector.All,
            FileFilters = fixture.Files,
            TitleFilter = fixture.TitleId,
            Churn = (baseOptions?.Churn ?? ChurnFilter.None).With(manifest.Ignore).With(fixture.Ignore),
            Decoder = decoder,
        };

    /// <summary>Null when the archive matches the recorded hash (or none is recorded), else an error.</summary>
    public static string? CheckArchive(SaveRef reference, string fullPath)
    {
        if (reference.Sha256 is null || Directory.Exists(fullPath))
        {
            return null;
        }

        using var stream = File.OpenRead(fullPath);
        var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        return string.Equals(actual, reference.Sha256, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"{reference.Archive}: SHA-256 is {actual}, the manifest records {reference.Sha256}.";
    }

    public static FixtureVerification Verify(Fixture fixture, ISaveSet before, ISaveSet after, SaveDiffOptions options)
    {
        var (report, files) = SaveSetDiffer.ComparePairs(before, after, options, $"{fixture.Id}:before", $"{fixture.Id}:after");
        var trees = new Dictionary<ISaveFile, SemanticTree>(ReferenceEqualityComparer.Instance);
        var results = fixture.Expected.Select(x => Check(x, files, options, trees)).ToList();
        return new FixtureVerification
        {
            FixtureId = fixture.Id,
            Report = report,
            Expectations = results,
            Unexpected = fixture.Strict ? Unexpected(fixture, files) : [],
        };
    }

    private static bool Matches(ExpectedChange x, SetEntry e) =>
        e.IsBackup == x.Backup && Glob.IsMatch(x.Directory, e.Directory) && Glob.IsMatch(x.File, e.Path);

    private static ExpectationResult Check(ExpectedChange x, IReadOnlyList<(PairedFile Pair, FileDiff Diff)> files, SaveDiffOptions options, Dictionary<ISaveFile, SemanticTree> trees)
    {
        var matched = files.Where(f => Matches(x, f.Pair.Any)).ToList();
        if (matched.Count == 0)
        {
            return new ExpectationResult(x, false, "no compared file matches");
        }

        foreach (var (pair, diff) in matched)
        {
            if (pair.Before is null || pair.After is null)
            {
                return new ExpectationResult(x, false, $"{diff.DisplayPath} is {diff.Status}, not present on both sides");
            }

            var failure = x.IsByteRange
                ? CheckBytes(x, pair.Before.File, pair.After.File)
                : CheckPath(x, pair.Before.File, pair.After.File, options, trees);
            if (failure is not null)
            {
                return new ExpectationResult(x, false, $"{diff.DisplayPath}: {failure}");
            }
        }

        return new ExpectationResult(x, true, $"ok in {matched.Count} file(s)");
    }

    private static string? CheckBytes(ExpectedChange x, ISaveFile before, ISaveFile after)
    {
        var b = ReadRange(before, x.Offset!.Value, x.Length!.Value);
        var a = ReadRange(after, x.Offset.Value, x.Length.Value);
        var bh = b is null ? ExpectedChange.Absent : Convert.ToHexStringLower(b);
        var ah = a is null ? ExpectedChange.Absent : Convert.ToHexStringLower(a);
        return Compare(x, bh, ah, StringComparison.OrdinalIgnoreCase);
    }

    private static string? CheckPath(ExpectedChange x, ISaveFile before, ISaveFile after, SaveDiffOptions options, Dictionary<ISaveFile, SemanticTree> trees)
    {
        if (options.Decoder is not { } decoder)
        {
            return "semantic expectation but no decoder configured";
        }

        try
        {
            var b = Lookup(Tree(decoder, before, options, trees), x.Path!);
            var a = Lookup(Tree(decoder, after, options, trees), x.Path!);
            return Compare(x, b, a, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            return $"decoder {decoder.Id} failed: {ex.Message}";
        }
    }

    private static string? Compare(ExpectedChange x, string before, string after, StringComparison comparison)
    {
        if (x.Before is not null && !string.Equals(x.Before, before, comparison))
        {
            return $"before is {before}, expected {x.Before}";
        }

        if (x.After is not null && !string.Equals(x.After, after, comparison))
        {
            return $"after is {after}, expected {x.After}";
        }

        var changed = !string.Equals(before, after, comparison);
        return changed == x.Changed ? null : changed ? $"changed ({before} → {after}) but expected unchanged" : $"unchanged ({before})";
    }

    private static SemanticTree Tree(ISaveDecoder decoder, ISaveFile file, SaveDiffOptions options, Dictionary<ISaveFile, SemanticTree> trees)
    {
        if (!trees.TryGetValue(file, out var tree))
        {
            tree = decoder.Decode(file, options.DecodeLimits);
            trees[file] = tree;
        }

        return tree;
    }

    private static string Lookup(SemanticTree tree, string path) =>
        tree.Values.FirstOrDefault(v => v.Path == path)?.Value ?? ExpectedChange.Absent;

    private static byte[]? ReadRange(ISaveFile file, long offset, int length)
    {
        if (offset + length > file.Length)
        {
            return null;
        }

        using var stream = file.OpenRead();
        var buffer = new byte[length];
        if (stream.CanSeek)
        {
            stream.Position = offset;
        }
        else
        {
            var skip = new byte[8192];
            for (var remaining = offset; remaining > 0;)
            {
                var read = stream.Read(skip, 0, (int)Math.Min(skip.Length, remaining));
                if (read == 0)
                {
                    return null;
                }

                remaining -= read;
            }
        }

        stream.ReadExactly(buffer);
        return buffer;
    }

    private static List<string> Unexpected(Fixture fixture, IReadOnlyList<(PairedFile Pair, FileDiff Diff)> files)
    {
        var unexpected = new List<string>();
        foreach (var (pair, diff) in files.Where(f => f.Diff.IsChange))
        {
            var covering = fixture.Expected.Where(x => x.Changed && Matches(x, pair.Any)).ToList();
            if (covering.Count == 0)
            {
                unexpected.Add($"{diff.DisplayPath}: {diff.Status} but no expectation covers this file");
                continue;
            }

            var ranges = covering.Where(x => x.IsByteRange).ToList();
            if (ranges.Count > 0 && diff.Bytes is { } bytes)
            {
                if (bytes.Truncated)
                {
                    unexpected.Add($"{diff.DisplayPath}: {bytes.RangeCount} ranges exceed the report cap; strict check incomplete");
                }

                unexpected.AddRange(bytes.Ranges
                    .Where(r => !ranges.Any(x => x.Offset < r.End && r.Offset < x.Offset + x.Length))
                    .Select(r => $"{diff.DisplayPath}: unexpected change at 0x{r.Offset:X}+{r.Length}"));
            }

            var paths = covering.Where(x => x.Path is not null).Select(x => x.Path!).ToHashSet(StringComparer.Ordinal);
            if (paths.Count > 0 && diff.Semantic is { } semantic)
            {
                if (semantic.Truncated)
                {
                    unexpected.Add($"{diff.DisplayPath}: {semantic.Total} semantic changes exceed the report cap; strict check incomplete");
                }

                unexpected.AddRange(semantic.Changes
                    .Where(c => !paths.Contains(c.Path))
                    .Select(c => $"{diff.DisplayPath}: unexpected {c.Kind.ToString().ToLowerInvariant()} {c.Path}"));
            }
        }

        return unexpected;
    }
}
