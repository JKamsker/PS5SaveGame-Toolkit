using System.Security.Cryptography;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Testing.Diff;

/// <summary>A file of one side, with its logical identity and its rank among the selected generations.</summary>
internal sealed record SetEntry(ISaveFile File, string TitleId, string Directory, bool IsBackup, string Path, int Generation, int Rank)
{
    /// <summary>Case-insensitive identity of the logical path (computed once; copied by <c>with</c>).</summary>
    public string PathKey { get; } = MakeKey(TitleId, Directory, IsBackup, Path);

    public static string MakeKey(string titleId, string directory, bool isBackup, string path) =>
        string.Join('\u0001', titleId.ToUpperInvariant(), directory.ToUpperInvariant(), isBackup ? "bu" : "main", path.ToUpperInvariant());
}

internal sealed record PairedFile(SetEntry? Before, SetEntry? After)
{
    public SetEntry Any => Before ?? After!;
}

/// <summary>Selects, pairs and fingerprints the files of two save sets.</summary>
internal static class SaveSetPairing
{
    public static List<SetEntry> Entries(ISaveSet set, SaveDiffOptions options, GenerationSelector selector)
    {
        var raw = new List<SetEntry>();
        foreach (var directory in set.Directories)
        {
            if (options.TitleFilter is { } title && !string.Equals(title, directory.TitleId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var files = options.IncludeSystemFiles ? directory.Files.Concat(directory.SystemFiles) : directory.Files;
            foreach (var file in files)
            {
                if (options.FileFilters.Count == 0 || options.FileFilters.Any(g => Glob.IsMatch(g, file.PathInDirectory) || Glob.IsMatch(g, $"{directory.RawName}/{file.PathInDirectory}")))
                {
                    raw.Add(new SetEntry(file, directory.TitleId, directory.Name, directory.IsBackup, file.PathInDirectory, directory.Generation, 0));
                }
            }
        }

        // Rank = position among the selected generations of the same path, so "all" pairs g0↔g0, g1↔g1
        // while "latest"/"N" pair the chosen copy with rank 0 of the other side.
        return [.. selector.Apply(raw, e => e.PathKey, e => e.Generation)
            .GroupBy(e => e.PathKey, StringComparer.Ordinal)
            .SelectMany(g => g.OrderBy(e => e.Generation).Select((e, i) => e with { Rank = i }))];
    }

    public static List<PairedFile> Pair(IReadOnlyList<SetEntry> before, IReadOnlyList<SetEntry> after)
    {
        static string Key(SetEntry e) => $"{e.PathKey}\u0002{e.Rank}";
        var afterByKey = after.ToDictionary(Key, StringComparer.Ordinal);
        var pairs = new List<PairedFile>();
        var matched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in before)
        {
            var key = Key(b);
            afterByKey.TryGetValue(key, out var a);
            if (a is not null)
            {
                matched.Add(key);
            }

            pairs.Add(new PairedFile(b, a));
        }

        pairs.AddRange(after.Where(a => !matched.Contains(Key(a))).Select(a => new PairedFile(null, a)));
        return [.. pairs
            .OrderBy(p => p.Any.TitleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Any.Directory, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Any.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Any.Rank)
            .ThenBy(p => p.Any.IsBackup)];
    }

    /// <summary>Compares each main file with its <c>sdimg_sce_bu_</c> twin of the same generation.</summary>
    public static TwinSummary Twins(IReadOnlyList<SetEntry> entries, Fingerprints fingerprints)
    {
        static string TwinKey(SetEntry e) => $"{SetEntry.MakeKey(e.TitleId, e.Directory, false, e.Path)}\u0002{e.Generation}";
        var mains = entries.Where(e => !e.IsBackup).GroupBy(TwinKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var backups = entries.Where(e => e.IsBackup).GroupBy(TwinKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var directories = entries.Select(e => DirKey(e, e.IsBackup)).ToHashSet(StringComparer.Ordinal);
        var identical = 0;
        var mismatches = new List<TwinCheck>();
        foreach (var (key, main) in mains)
        {
            if (!backups.TryGetValue(key, out var backup))
            {
                // Directories without any backup twin are common (not every title uses one); only report
                // missing files when the backup directory itself exists.
                if (directories.Contains(DirKey(main, isBackup: true)))
                {
                    mismatches.Add(Check(main, TwinStatus.MissingBackup));
                }

                continue;
            }

            if (fingerprints.Sha256(main.File) == fingerprints.Sha256(backup.File))
            {
                identical++;
            }
            else
            {
                mismatches.Add(Check(main, TwinStatus.Differs));
            }
        }

        mismatches.AddRange(backups.Where(kv => !mains.ContainsKey(kv.Key)
                && directories.Contains(DirKey(kv.Value, isBackup: false)))
            .Select(kv => Check(kv.Value, TwinStatus.MissingMain)));
        return new TwinSummary { Identical = identical, Mismatches = mismatches };

        static string DirKey(SetEntry e, bool isBackup) => SetEntry.MakeKey(e.TitleId, e.Directory, isBackup, "") + e.Generation;

        static TwinCheck Check(SetEntry e, TwinStatus status) => new(e.TitleId, e.Directory, e.Path, e.Generation, status);
    }
}

/// <summary>SHA-256 per file, computed once (streaming) and reused; imported files already carry it.</summary>
internal sealed class Fingerprints
{
    private readonly Dictionary<ISaveFile, string> _cache = new(ReferenceEqualityComparer.Instance);

    public string Sha256(ISaveFile file)
    {
        if (file is DiskSaveFile disk)
        {
            return disk.Sha256;
        }

        if (!_cache.TryGetValue(file, out var hash))
        {
            using var stream = file.OpenRead();
            hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            _cache[file] = hash;
        }

        return hash;
    }
}
