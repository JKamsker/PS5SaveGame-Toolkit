using System.Text.Json;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Presets;

/// <summary>Finite file I/O shared by reload, CRUD and staged import publication.</summary>
internal sealed class PresetLibraryStorage(PresetStoreOptions options, JsonSerializerOptions jsonOptions)
{
    private const int ReadBufferSize = 64 * 1024;
    private string Path => System.IO.Path.Combine(options.StorageRoot, "presets.json");

    internal IReadOnlyList<ToolkitPreset> Read(bool allowMissing)
    {
        if (!File.Exists(Path))
        {
            if (allowMissing) return [];
            throw new InvalidDataException("Preset storage disappeared during reload.");
        }
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
            return JsonSerializer.Deserialize<List<ToolkitPreset>>(content.ToArray(), jsonOptions) ?? [];
        }
        catch (FileNotFoundException ex) { throw new InvalidDataException("Preset storage disappeared during load.", ex); }
        catch (DirectoryNotFoundException ex) { throw new InvalidDataException("Preset storage disappeared during load.", ex); }
        catch (JsonException ex) { throw new InvalidDataException("Preset storage is not valid JSON.", ex); }
    }

    internal byte[] Serialize(IEnumerable<ToolkitPreset> records)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(records.OrderBy(p => p.Id), jsonOptions);
        if (bytes.LongLength > options.MaxPayloadBytes)
            throw new InvalidOperationException("Preset storage exceeds the configured size limit.");
        return bytes;
    }

    internal void Write(IEnumerable<ToolkitPreset> records)
    {
        var bytes = Serialize(records);
        var temp = Path + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, Path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            throw new PresetStorageException("Preset storage is temporarily unavailable. Retry the operation.", ex);
        }
    }
}
