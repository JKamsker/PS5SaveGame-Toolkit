using System.IO.Compression;
using System.Text.Json;
using SaveToolkit.Plugins.GenericJson;
using SaveToolkit.Plugins.XorJson;
using SaveToolkit.Sdk;

namespace SaveToolkit.Benchmarks;

public enum DataSize { Small, Medium, Large }
public enum SaveFormat { GenericJson, XorJson }

internal sealed class SyntheticData
{
    private static readonly DateTimeOffset ZipTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public SyntheticData(DataSize size, SaveFormat format)
    {
        (SaveCount, RowCount) = size switch
        {
            DataSize.Small => (2, 256),
            DataSize.Medium => (16, 2_000),
            DataSize.Large => (64, 20_000),
            _ => throw new ArgumentOutOfRangeException(nameof(size)),
        };
        Plugin = format == SaveFormat.GenericJson ? new GenericJsonPlugin() : new XorJsonPlugin();
        var json = JsonSerializer.SerializeToUtf8Bytes(Enumerable.Range(0, RowCount));
        // XorJson supports byte-minus-one saves as well as XOR. Use XOR-1 consistently here.
        SaveBytes = format == SaveFormat.GenericJson ? json : json.Select(value => (byte)(value ^ 1)).ToArray();
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var index = 0; index < SaveCount; index++)
            {
                var entry = zip.CreateEntry($"PPSA99998/sdimg_BENCH{index:D4}/data.sav", CompressionLevel.Fastest);
                entry.LastWriteTime = ZipTimestamp;
                using var output = entry.Open();
                output.Write(SaveBytes);
            }
        }
        ZipBytes = buffer.ToArray();
    }

    public int SaveCount { get; }
    public int RowCount { get; }
    public IGamePlugin Plugin { get; }
    public byte[] SaveBytes { get; }
    public byte[] ZipBytes { get; }
}
