using System.Buffers;
using System.IO.Compression;
using System.Reflection;

namespace SaveToolkit.Core.References;

/// <summary>Read-only view of the files of one pack (a zip archive or a plugin's embedded resources).</summary>
internal interface IPackFileSource
{
    /// <summary>Human description for error messages, e.g. "plugin test.items, refpacks/items-en".</summary>
    string Origin { get; }

    /// <summary>Reads a pack-relative file, or null when it does not exist. Throws when it exceeds <paramref name="maxBytes"/>.</summary>
    byte[]? Read(string path, long maxBytes);
}

internal static class PackFileReader
{
    private const int ChunkSize = 81920;

    /// <summary>Copies at most <paramref name="maxBytes"/>; does not trust declared lengths.</summary>
    public static byte[] ReadBounded(Stream stream, long maxBytes, string what)
    {
        // Seekable streams (embedded resources) report their real length: read it exactly, with no
        // per-file scratch chunk. Packs with many small icons otherwise pay ~80 KB per file.
        if (stream.CanSeek)
        {
            var remaining = stream.Length - stream.Position;
            if (remaining > maxBytes)
            {
                throw new ReferencePackException($"{what} is larger than the limit of {maxBytes:N0} bytes.");
            }

            var exact = new byte[remaining];
            stream.ReadExactly(exact);
            return exact;
        }

        using var buffer = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            int read;
            while ((read = stream.Read(chunk, 0, ChunkSize)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw new ReferencePackException($"{what} is larger than the limit of {maxBytes:N0} bytes.");
                }

                buffer.Write(chunk, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        return buffer.ToArray();
    }
}

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

/// <summary>
/// Embedded resources named <c>refpacks/&lt;folder&gt;/&lt;path&gt;</c> in a plugin assembly, e.g. from
/// <c>&lt;EmbeddedResource Include="ReferencePacks\**\*" LogicalName="refpacks/%(RecursiveDir)%(Filename)%(Extension)" /&gt;</c>.
/// Backslashes from <c>%(RecursiveDir)</c> on Windows are normalised to '/'.
/// </summary>
internal sealed class EmbeddedPackSource(Assembly assembly, string folder, IReadOnlyDictionary<string, string> resources) : IPackFileSource
{
    public const string Prefix = "refpacks/";

    public string Origin => $"{assembly.GetName().Name}, {Prefix}{folder}";

    public byte[]? Read(string path, long maxBytes)
    {
        if (!resources.TryGetValue(path, out var name) || assembly.GetManifestResourceStream(name) is not { } stream)
        {
            return null;
        }

        using (stream)
        {
            return PackFileReader.ReadBounded(stream, maxBytes, $"'{path}'");
        }
    }

    /// <summary>Groups an assembly's <c>refpacks/</c> resources by pack folder.</summary>
    public static IReadOnlyList<EmbeddedPackSource> Discover(Assembly assembly)
    {
        var folders = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var normalised = name.Replace('\\', '/');
            if (!normalised.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = normalised[Prefix.Length..];
            var slash = rest.IndexOf('/', StringComparison.Ordinal);
            if (slash <= 0)
            {
                continue;
            }

            var folder = rest[..slash];
            if (!folders.TryGetValue(folder, out var files))
            {
                folders[folder] = files = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            files[rest[(slash + 1)..]] = name;
        }

        return [.. folders.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => new EmbeddedPackSource(assembly, f.Key, f.Value))];
    }
}
