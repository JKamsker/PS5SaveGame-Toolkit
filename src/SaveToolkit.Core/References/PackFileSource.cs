using System.IO.Compression;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.References;

/// <summary>
/// A pack zip with <c>pack.json</c> at the root or inside exactly one top-level folder. Entry names are
/// only matched against validated manifest paths; nothing is extracted to disk.
/// </summary>
internal sealed class ZipPackSource : IPackFileSource, IDisposable
{
    private readonly ZipArchive _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.Ordinal);
    private readonly string _prefix;

    public ZipPackSource(ReadOnlyMemory<byte> archive, string origin, ReferencePackLimits limits)
    {
        Origin = origin;
        try
        {
            _zip = new ZipArchive(new MemoryStream(archive.ToArray(), writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            throw new ReferencePackException($"{origin} is not a valid zip archive: {ex.Message}", ex);
        }

        if (_zip.Entries.Count > limits.MaxFiles)
        {
            throw new ReferencePackException($"{origin} contains more than {limits.MaxFiles:N0} entries.");
        }

        foreach (var entry in _zip.Entries.Where(e => !e.FullName.EndsWith('/')))
        {
            _entries.TryAdd(entry.FullName, entry);
        }

        _prefix = FindRoot(_entries.Keys) ?? throw new ReferencePackException($"{origin} has no {PackManifest.FileName} at its root.");
    }

    public string Origin { get; }

    public byte[]? Read(string path, long maxBytes)
    {
        if (!_entries.TryGetValue(_prefix + path, out var entry))
        {
            return null;
        }

        if (entry.Length > maxBytes)
        {
            throw new ReferencePackException($"'{path}' is larger than the limit of {maxBytes:N0} bytes.");
        }

        try
        {
            using var stream = entry.Open();
            return PackFileReader.ReadBounded(stream, maxBytes, $"'{path}'");
        }
        catch (InvalidDataException ex)
        {
            throw new ReferencePackException($"'{path}' could not be decompressed: {ex.Message}", ex);
        }
    }

    public void Dispose() => _zip.Dispose();

    private static string? FindRoot(IEnumerable<string> names)
    {
        var list = names.ToList();
        if (list.Contains(PackManifest.FileName))
        {
            return string.Empty;
        }

        var nested = list.Where(n => n.EndsWith("/" + PackManifest.FileName, StringComparison.Ordinal) && n.Count(c => c == '/') == 1).ToList();
        return nested.Count == 1 ? nested[0][..^PackManifest.FileName.Length] : null;
    }
}
