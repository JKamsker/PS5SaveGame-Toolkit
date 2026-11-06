using System.Text.Json;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Presets;

/// <summary>Finite file I/O shared by reload, CRUD and staged import publication.</summary>
internal sealed class PresetLibraryStorage(PresetStoreOptions options, JsonSerializerOptions jsonOptions)
{
    internal const string FileName = "presets.json";
    private const int ReadBufferSize = 64 * 1024;
    private string Path => System.IO.Path.Combine(options.StorageRoot, FileName);

    internal PresetStorageSnapshot Read(bool allowMissing)
    {
        try
        {
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, ReadBufferSize, FileOptions.SequentialScan);
            using var content = new MemoryStream(Math.Min(options.MaxPayloadBytes, ReadBufferSize));
            var buffer = new byte[ReadBufferSize];
            long count = 0;
            while (true)
            {
                var remaining = options.MaxPayloadBytes - count;
                var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining + 1));
                if (read == 0) break;
                count = checked(count + read);
                if (count > options.MaxPayloadBytes)
                    throw new InvalidDataException("Preset storage exceeds the configured size limit.");
                content.Write(buffer, 0, read);
            }
            var bytes = content.ToArray();
            return new(JsonSerializer.Deserialize<List<ToolkitPreset>>(bytes, jsonOptions)
                ?? throw new InvalidDataException("Preset storage must contain a record array."),
                PresetStorageIdentity.Fingerprint(bytes));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            if (RootBlocked(options.StorageRoot))
                throw new PresetStorageException("Preset storage is temporarily unavailable. Retry the operation.", ex);
            if (allowMissing) return new([], PresetStorageIdentity.Missing);
            throw new InvalidDataException("Preset storage disappeared during load.", ex);
        }
        catch (JsonException ex) { throw new InvalidDataException("Preset storage is not valid JSON.", ex); }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PresetStorageException("Preset storage is temporarily unavailable. Retry the operation.", ex);
        }
    }

    /// <summary>
    /// Whether a non-directory occupies the storage root or one of its ancestors. The OS reports that
    /// (ENOTDIR, ERROR_PATH_NOT_FOUND) as <see cref="DirectoryNotFoundException"/>, exactly like a
    /// missing root, but the library is then unreachable rather than absent: treating it as an empty
    /// library would hide the stored presets and turn a storage failure into a reload conflict.
    /// </summary>
    internal static bool RootBlocked(string root)
    {
        for (var path = System.IO.Path.GetFullPath(root); !string.IsNullOrEmpty(path); path = System.IO.Path.GetDirectoryName(path))
        {
            if (Directory.Exists(path)) return false;
            if (File.Exists(path)) return true;
        }
        return false;
    }

    internal byte[] Serialize(IEnumerable<ToolkitPreset> records)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(records.OrderBy(p => p.Id), jsonOptions);
        if (bytes.LongLength > options.MaxPayloadBytes)
            throw new InvalidOperationException("Preset storage exceeds the configured size limit.");
        return bytes;
    }

    internal string Write(IEnumerable<ToolkitPreset> records)
    {
        var bytes = Serialize(records);
        var temp = Path + ".tmp";
        try
        {
            // The root can disappear under a running store (a browser tab reloading IndexedDB drops
            // empty directories it never flushed); recreate it like the reference pack store does.
            Directory.CreateDirectory(options.StorageRoot);
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, Path, overwrite: true);
            return PresetStorageIdentity.Fingerprint(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            throw new PresetStorageException("Preset storage is temporarily unavailable. Retry the operation.", ex);
        }
    }
}

internal sealed record PresetStorageSnapshot(IReadOnlyList<ToolkitPreset> Records, string Fingerprint);
