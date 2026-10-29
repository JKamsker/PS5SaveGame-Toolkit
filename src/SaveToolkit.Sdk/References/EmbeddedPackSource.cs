using System.Buffers;
using System.Reflection;

namespace SaveToolkit.Sdk.References;

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
                throw new EmbeddedReferencePackException($"{what} is larger than the limit of {maxBytes:N0} bytes.");
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
                    throw new EmbeddedReferencePackException($"{what} is larger than the limit of {maxBytes:N0} bytes.");
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
/// Embedded resources named <c>refpacks/&lt;folder&gt;/&lt;path&gt;</c> in a plugin assembly, e.g. from
/// <c>&lt;EmbeddedResource Include="ReferencePacks\**\*" LogicalName="refpacks/%(RecursiveDir)%(Filename)%(Extension)" /&gt;</c>.
/// Backslashes from <c>%(RecursiveDir)</c> on Windows are normalised to '/'.
/// </summary>
internal sealed class EmbeddedPackSource(Assembly assembly, string folder,
    IReadOnlyDictionary<string, string> resources, string? rejection = null) : IPackFileSource
{
    public const string Prefix = "refpacks/";

    public string Origin => $"{assembly.GetName().Name}, {Prefix}{folder}";

    public byte[]? Read(string path, long maxBytes)
    {
        if (rejection is not null)
        {
            throw new EmbeddedReferencePackException(rejection);
        }

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
        var names = new Dictionary<string, (string Name, string Folder)>(StringComparer.OrdinalIgnoreCase);
        var rejections = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var normalised = name.Replace('\\', '/');
            if (!normalised.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
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
            if (!names.TryAdd(normalised, (name, folder)))
            {
                var previous = names[normalised];
                var message = $"Embedded resources '{previous.Name}' and '{name}' in {assembly.GetName().Name} " +
                    "have the same path after separator and case normalization.";
                rejections.TryAdd(previous.Folder, message);
                rejections.TryAdd(folder, message);
            }

            // Case variants of the prefix still collide, but do not introduce new pack discovery rules.
            if (!normalised.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (!folders.TryGetValue(folder, out var files))
            {
                folders[folder] = files = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            // A collision rejects every affected folder, including unlisted files. Never read a winner.
            files.TryAdd(rest[(slash + 1)..], name);
        }

        return [.. folders.OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => new EmbeddedPackSource(assembly, f.Key, f.Value, rejections.GetValueOrDefault(f.Key)))];
    }
}
