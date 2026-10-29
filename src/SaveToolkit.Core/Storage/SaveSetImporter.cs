using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Storage;

/// <summary>An uploaded file: a .zip dump or a single file whose name may carry a relative path.</summary>
public sealed record ImportSource(string Name, Func<Stream> Open);

public sealed record ImportLimits
{
    public long MaxTotalBytes { get; init; } = 8L * 1024 * 1024 * 1024;
    public int MaxEntries { get; init; } = 50_000;
}

public sealed class ImportException(string message) : Exception(message);

/// <summary>
/// Turns decrypted PS5 save dumps (as produced by common save managers:
/// <c>PPSAxxxxx/sdimg_[sce_bu_]&lt;dir&gt;/...</c>) into a <see cref="DiskSaveSet"/>.
/// Paths that occur several times (appended snapshots) are split into generations.
/// Content is copied into numbered blobs so archive names never touch the file system.
/// </summary>
public static partial class SaveSetImporter
{
    private const string UnknownTitle = "UNKNOWN";
    // SFO is optional display metadata. Oversized files remain exportable without parsing them in memory.
    private const int MaxMetadataBytes = 1024 * 1024;
    // ZIP APPNOTE appendix D: unflagged names use CP437; ZipArchive still honors flagged UTF-8 names.
    private static readonly Encoding LegacyZipEncoding = CodePagesEncodingProvider.Instance.GetEncoding(437)!;

    public static DiskSaveSet Import(IEnumerable<ImportSource> sources, string storageRoot, ImportLimits? limits = null)
    {
        limits ??= new ImportLimits();
        System.IO.Directory.CreateDirectory(storageRoot);
        using var builder = new Builder(storageRoot, limits);
        foreach (var source in sources)
        {
            if (source.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ImportArchive(source, builder);
            }
            else
            {
                using var stream = source.Open();
                builder.Add(source.Name, stream);
            }
        }

        var set = builder.Build();
        builder.Commit();
        return set;
    }

    private static void ImportArchive(ImportSource source, Builder builder)
    {
        try
        {
            using var stream = source.Open();
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: LegacyZipEncoding);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
                try
                {
                    using var entryStream = entry.Open();
                    builder.Add(entry.FullName, entryStream, entry.Length, entry.Crc32);
                }
                catch (InvalidDataException ex)
                {
                    throw new InvalidDataException($"Entry '{entry.FullName}': {ex.Message}", ex);
                }
            }
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"Could not import archive '{source.Name}': {ex.Message}", ex);
        }
    }

    internal sealed record ParsedPath(string? TitleId, string RawDirectory, string PathInDirectory);

    /// <summary>Splits an archive path into title / save directory / path inside the directory.</summary>
    internal static ParsedPath? ParsePath(string path)
    {
        var segments = PathSegments(path);
        if (segments.Length == 0 || segments.Any(s => !IsSafeSegment(s)) || IsMetadataPath(segments))
        {
            return null;
        }

        var dirIndex = Array.FindIndex(segments, s => s.StartsWith("sdimg_", StringComparison.OrdinalIgnoreCase));
        if (dirIndex >= 0)
        {
            if (dirIndex == segments.Length - 1)
            {
                return null;
            }

            var title = segments.Take(dirIndex).LastOrDefault(s => TitleIdRegex().IsMatch(s))?.ToUpperInvariant();
            return new ParsedPath(title, segments[dirIndex], string.Join('/', segments[(dirIndex + 1)..]));
        }

        var titleIndex = Array.FindIndex(segments, s => TitleIdRegex().IsMatch(s));
        var anyTitle = titleIndex < 0 ? null : segments[titleIndex].ToUpperInvariant();
        var rootIndex = titleIndex < 0 ? 0 : titleIndex + 1;
        if (rootIndex < segments.Length - 1)
            return new ParsedPath(anyTitle, segments[rootIndex], string.Join('/', segments[(rootIndex + 1)..]));

        return new ParsedPath(anyTitle, "loose", segments[^1]);
    }

    private static string[] PathSegments(string path) => path.Replace('\\', '/')
        .Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => s != ".").ToArray();

    private static bool IsMetadataPath(string[] segments) => segments.Contains("__MACOSX", StringComparer.OrdinalIgnoreCase);

    private static bool IsSafeSegment(string s) =>
        s is not ("." or "..") && s.IndexOfAny(['\0', ':', '*', '?', '"', '<', '>', '|']) < 0 && s.Length <= 255;

    [GeneratedRegex("^[A-Z]{4}[0-9]{5}$", RegexOptions.IgnoreCase)]
    private static partial Regex TitleIdRegex();

    private sealed class Builder(string storageRoot, ImportLimits limits) : IDisposable
    {
        private readonly Dictionary<string, int> _occurrences = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _directoryNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<PendingFile> _files = [];
        private readonly List<string> _createdBlobs = [];
        private long _totalBytes;
        private bool _committed;

        public void Commit() => _committed = true;

        public void Dispose()
        {
            if (_committed) return;
            List<Exception> errors = [];
            foreach (var path in _createdBlobs)
            {
                try { File.Delete(path); }
                catch (IOException ex) { errors.Add(ex); }
                catch (UnauthorizedAccessException ex) { errors.Add(ex); }
            }
            if (errors.Count > 0)
                throw new IOException("Import failed and temporary blobs could not all be removed.", new AggregateException(errors));
        }

        public void Add(string archivePath, Stream content, long? expectedLength = null, uint? expectedCrc32 = null)
        {
            if (IsMetadataPath(PathSegments(archivePath))) return;
            var parsed = ParsePath(archivePath);
            if (parsed is null)
            {
                throw new ImportException($"Cannot import file '{archivePath}': unsupported or invalid path (maximum name length: 255 characters).");
            }

            var directoryKey = $"{parsed.TitleId}/{parsed.RawDirectory}";
            if (_directoryNames.TryGetValue(directoryKey, out var rawDirectory))
                parsed = parsed with { RawDirectory = rawDirectory };
            else
                _directoryNames.Add(directoryKey, parsed.RawDirectory);

            if (_files.Count >= limits.MaxEntries)
            {
                throw new ImportException($"Too many files (limit {limits.MaxEntries}).");
            }

            var key = $"{parsed.TitleId}/{parsed.RawDirectory}/{parsed.PathInDirectory}";
            var generation = _occurrences.GetValueOrDefault(key);
            _occurrences[key] = generation + 1;

            var index = _files.Count;
            var blobPath = Path.Combine(storageRoot, $"{index:D6}.blob");
            var (length, sha) = CopyToBlob(content, blobPath, expectedLength, expectedCrc32);
            _files.Add(new PendingFile(parsed, generation, blobPath, length, sha));
        }

        private (long Length, string Sha256) CopyToBlob(Stream content, string blobPath, long? expectedLength, uint? expectedCrc32)
        {
            if (expectedLength > limits.MaxTotalBytes - _totalBytes)
                throw new ImportException($"Upload exceeds {limits.MaxTotalBytes / (1024 * 1024)} MiB.");

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var checksum = expectedCrc32.HasValue ? new ZipEntryChecksum() : null;
            using var output = new FileStream(blobPath, FileMode.CreateNew, FileAccess.Write);
            // Track only files we created: a name collision must never delete someone else's data.
            _createdBlobs.Add(blobPath);
            var buffer = new byte[81920];
            long length = 0;
            int read;
            while ((read = content.Read(buffer)) > 0)
            {
                length += read;
                _totalBytes += read;
                if (expectedLength is { } expected && length > expected)
                {
                    throw new InvalidDataException($"Decompressed data exceeds the declared length of {expected} bytes.");
                }
                if (_totalBytes > limits.MaxTotalBytes)
                {
                    throw new ImportException($"Upload exceeds {limits.MaxTotalBytes / (1024 * 1024)} MiB.");
                }

                output.Write(buffer, 0, read);
                hash.AppendData(buffer, 0, read);
                checksum?.Append(buffer.AsSpan(0, read));
            }

            if (expectedLength is { } declared && length != declared)
            {
                throw new InvalidDataException($"Truncated entry: expected {declared} bytes but read {length}.");
            }
            if (expectedCrc32 is { } crc32 && checksum!.Value != crc32)
            {
                throw new InvalidDataException("Entry CRC-32 does not match the imported data; the archive may be corrupt or truncated.");
            }

            return (length, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }

        public DiskSaveSet Build()
        {
            if (_files.Count == 0)
            {
                throw new ImportException("No save files found in the upload.");
            }

            var set = new DiskSaveSet(storageRoot);
            var groups = _files
                .GroupBy(f => (f.Path.TitleId, Raw: f.Path.RawDirectory, f.Generation))
                .OrderBy(g => g.Key.Generation)
                .ThenBy(g => g.Key.TitleId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => DiskSaveDirectory.SplitRawName(g.Key.Raw).Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Key.Raw.Length);

            var dirIndex = 0;
            var fileIndex = 0;
            foreach (var group in groups)
            {
                var sfoFile = group.FirstOrDefault(f => string.Equals(f.Path.PathInDirectory, "sce_sys/param.sfo", StringComparison.OrdinalIgnoreCase));
                var sfo = sfoFile is null || sfoFile.Length > MaxMetadataBytes
                    ? null : ParamSfo.TryParse(File.ReadAllBytes(sfoFile.BlobPath));
                var title = group.Key.TitleId ?? NormalizeTitle(sfo?.TitleId) ?? UnknownTitle;

                var directory = new DiskSaveDirectory(set, $"d{dirIndex++}", title, group.Key.Raw, group.Key.Generation) { ParamSfo = sfo };
                foreach (var f in group.OrderBy(f => f.Path.PathInDirectory, StringComparer.OrdinalIgnoreCase))
                {
                    var file = new DiskSaveFile(directory, $"f{fileIndex++}", f.Path.PathInDirectory, f.BlobPath, f.Length, f.Sha256);
                    (file.IsSystemFile ? directory.SystemFileList : directory.FileList).Add(file);
                }

                set.Add(directory);
            }

            return set;
        }

        private static string? NormalizeTitle(string? id) =>
            id is not null && TitleIdRegex().IsMatch(id) ? id.ToUpperInvariant() : null;

        private sealed record PendingFile(ParsedPath Path, int Generation, string BlobPath, long Length, string Sha256);
    }
}
