using System.Text.Json;
using SaveToolkit.Core.Presets;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

/// <summary>Measures real durable serialization; compact input and post-choice storage have different bounds.</summary>
internal sealed class PresetReplacementSizeFixture
{
    internal const int Count = 200;
    private static readonly JsonSerializerOptions CompactOptions = new(JsonSerializerDefaults.Web);
    internal required ToolkitPreset[] Incoming { get; init; }
    internal required ToolkitPreset[] Existing { get; init; }
    internal required byte[] Package { get; init; }
    internal required int TextLength { get; init; }
    internal required long IncomingBytes { get; init; }
    internal required long ResultBytes { get; init; }

    internal static PresetReplacementSizeFixture Create()
    {
        const int initialLength = 1950;
        var initial = Build(initialLength);
        // One extra ASCII parameter character adds exactly Count bytes to each representation.
        // Calibrate once, then independently assert all actual serializer lengths below.
        var minimumDelta = (int)Math.Floor((PresetPackageCodec.MaxBytes - initial.IncomingBytes) / (decimal)Count) + 1;
        var maximumDelta = (int)Math.Min(
            Math.Floor((PresetPackageCodec.MaxBytes - initial.Package.Length) / (decimal)Count),
            Math.Floor((PresetPackageCodec.MaxBytes - initial.ResultBytes) / (decimal)Count));
        Assert.True(minimumDelta <= maximumDelta, "The fixture must straddle the actual durable bound.");
        var calibrated = Build(initialLength + minimumDelta + (maximumDelta - minimumDelta) / 2);
        Assert.InRange(calibrated.TextLength, 1, 4096);
        Assert.InRange(calibrated.Package.Length, 1, PresetPackageCodec.MaxBytes);
        Assert.True(calibrated.IncomingBytes > PresetPackageCodec.MaxBytes);
        Assert.InRange(calibrated.ResultBytes, 1, PresetPackageCodec.MaxBytes);
        Assert.Equal(Count, PresetPackageCodec.Decode(calibrated.Package).Presets.Count);
        return calibrated;
    }

    private static PresetReplacementSizeFixture Build(int textLength)
    {
        var incoming = Enumerable.Range(0, Count).Select(index => Record(index, 128, new string('x', textLength))).ToArray();
        var existing = Enumerable.Range(0, Count).Select(index => Record(index, 32, "old")).Append(
            Record(Count, 32, "untouched") with { Name = "Unrelated sentinel" }).ToArray();
        var result = incoming.Select((preset, index) => preset with { Id = existing[index].Id }).Append(existing[^1]).ToArray();
        return new()
        {
            Incoming = incoming, Existing = existing, TextLength = textLength,
            Package = JsonSerializer.SerializeToUtf8Bytes(new PresetPackage("1", new("Toolkit fixture", DateTimeOffset.UnixEpoch), incoming),
                CompactOptions),
            IncomingBytes = Measure(incoming), ResultBytes = Measure(result),
        };
    }

    private static ToolkitPreset Record(int index, int idLength, string text) => new("1",
        index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + new string('i', idLength - 3),
        $"Record{index:D3}", new("unknown-later", "1", ["OTHER"]),
        [new("flag", new Dictionary<string, JsonElement>
        {
            ["text"] = JsonSerializer.SerializeToElement(text),
            ["huge"] = JsonSerializer.Deserialize<JsonElement>("900719925474099312345678901"),
            ["lexeme"] = JsonSerializer.Deserialize<JsonElement>("1.2300e+120"),
        }, [])], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static long Measure(ToolkitPreset[] records)
    {
        // Only the measurement store has extra capacity, so it can measure an otherwise rejected representation.
        // The API host, final reopen and all transaction assertions retain the production 512 KiB bound.
        var root = TestZips.TempDir();
        var oracle = new PresetStore(new() { StorageRoot = root, MaxPayloadBytes = 2 * PresetPackageCodec.MaxBytes });
        oracle.ImportLibrary(oracle.Snapshot().Identity, records);
        return new FileInfo(Path.Combine(root, "presets.json")).Length;
    }
}
