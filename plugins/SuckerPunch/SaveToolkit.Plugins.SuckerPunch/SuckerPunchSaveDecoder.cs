using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>Native evidence paths, independent of reference labels, view pages and presentation text.</summary>
internal sealed class SuckerPunchSaveDecoder : ISaveDecoderHook
{
    private const int HeaderWordCount = 4;
    private const int HeaderPathDepth = 2;
    private const int InventoryPathDepth = 3;
    public string Id => "suckerpunch.native-v1";

    public bool CanDecode(ISaveFile file) => new SuckerPunchPlugin().Probe(file).IsMatch;

    public void Decode(ISaveFile file, ISaveDecodeSink sink)
    {
        if (file.Length > sink.MaxFileBytes)
        {
            throw new SaveFormatException("The Sucker Punch save exceeds the decoder file limit.");
        }

        if (!CanDecode(file))
        {
            throw new SaveFormatException("This is not a recognised Sucker Punch save file.");
        }

        var bytes = file.ReadAllBytes();
        var container = SaveContainer.Parse(bytes, file.Directory.TitleId);
        if (sink.MaxDepth < HeaderPathDepth)
        {
            sink.MarkTruncated("Container paths require a decode depth of at least 2.");
            return;
        }

        for (var index = 0; index < HeaderWordCount; index++)
        {
            var word = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint)));
            if (!sink.Add($"container/word{index}", "integer", word.ToString(CultureInfo.InvariantCulture))) return;
        }

        if (container.Kind != ContainerKind.Tsushima)
        {
            sink.Warn("Yōtei header words are raw evidence; gameplay quantities and integrity coverage remain unknown.");
            return;
        }

        var content = bytes.AsSpan(0, (int)container.Word1);
        var progress = TsushimaProgress.Parse(content, container.Word3);
        if (progress is not null)
        {
            sink.Warn("Difficulty and play-time meanings are inferred; no console validation is claimed.");
            if (!sink.Add("progress/difficulty", "text", progress.Difficulty)) return;
            if (progress.PlaySeconds is { } seconds &&
                !sink.Add(TsushimaPlayTime.ValueId, "float", seconds.ToString("R", CultureInfo.InvariantCulture))) return;
        }

        var inventory = TsushimaInventory.Parse(content);
        if (inventory is null) return;
        if (sink.MaxDepth < InventoryPathDepth)
        {
            sink.MarkTruncated("Inventory identity paths require a decode depth of at least 3.");
            return;
        }

        sink.Warn("Inventory quantities retain raw IDs; resource meanings are inferred and ownership is not established.");
        foreach (var record in inventory.Records.OrderBy(record => record.Name, StringComparer.Ordinal))
        {
            var quantity = BinaryPrimitives.ReadUInt32LittleEndian(content[record.QuantityOffset..]);
            if (!sink.Add(record.ValueId, "integer", quantity.ToString(CultureInfo.InvariantCulture))) return;
            if (!record.IsAmmo) continue;
            var copy = BinaryPrimitives.ReadUInt32LittleEndian(content[(record.QuantityOffset + sizeof(uint))..]);
            if (!sink.Add(record.MirrorValueId, "integer", copy.ToString(CultureInfo.InvariantCulture))) return;
        }
    }
}
