using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>LSOF v5–v7: section order and compact/adjacency layouts follow LSLib.</summary>
internal static class LsfReader
{
    internal static LarianResource Read(byte[] bytes, ResourceBudget? budget = null)
    {
        BinaryData.Require(bytes.Length >= LsfLayout.MetadataOffset && bytes.AsSpan().StartsWith("LSOF"u8), "Not an LSOF resource.");
        var version = BinaryData.U32(bytes, LsfLayout.Version);
        BinaryData.Require(version is >= 5 and <= 7, $"Unsupported LSOF version {version}; expected 5, 6 or 7.");
        var pairs = version >= 6 ? LsfLayout.V6PairCount : LsfLayout.V5PairCount;
        var flagsOffset = LsfLayout.MetadataOffset + pairs * LsfLayout.SizePairLength;
        var position = flagsOffset + LsfLayout.MetadataTrailerSize;
        BinaryData.Slice(bytes, 0, position);
        var flags = bytes[flagsOffset];
        var metadataFormat = BinaryData.U32(bytes, flagsOffset + LsfLayout.MetadataFormatOffset);
        BinaryData.Require(metadataFormat <= 2, "Unsupported LSOF metadata format.");
        _ = LarianCompression.Method(flags);
        var sizePairs = new (int Expanded, int Stored)[pairs];
        long totalExpanded = 0;
        for (var i = 0; i < pairs; i++)
        {
            var offset = LsfLayout.MetadataOffset + i * LsfLayout.SizePairLength;
            sizePairs[i] = (BinaryData.Size(BinaryData.U32(bytes, offset)), BinaryData.Size(BinaryData.U32(bytes, offset + sizeof(uint))));
            totalExpanded += sizePairs[i].Expanded;
        }

        BinaryData.Require(totalExpanded <= BinaryData.MaxBufferSize, "LSOF expanded sections exceed 256 MiB.");
        var pairShift = version >= 6 ? 0 : 1;
        var nodePair = LsfLayout.NodesPair - pairShift;
        var attributePair = LsfLayout.AttributesPair - pairShift;
        var recordSize = metadataFormat == 1 ? LsfLayout.AdjacencyRecordSize : LsfLayout.CompactRecordSize;
        (budget ?? new ResourceBudget()).Reserve(totalExpanded,
            sizePairs[nodePair].Expanded / recordSize, sizePairs[attributePair].Expanded / recordSize);
        var strings = ReadSection(LsfLayout.StringsPair, false);
        // Keys are declared second in v6+ metadata but physically stored LAST.
        var nodes = ReadSection(nodePair, true);
        var attributes = ReadSection(attributePair, true);
        var values = ReadSection(LsfLayout.ValuesPair - pairShift, true);
        var keys = version >= 6 ? ReadSection(LsfLayout.KeysPair, true) : [];
        var names = LsfTables.ReadNames(strings);
        var model = LsfTables.Read(names, nodes, attributes, values, keys, metadataFormat == 1);
        return model with { Format = $"LSOF v{version} · metadata {metadataFormat}" };

        byte[] ReadSection(int index, bool allowFrame)
        {
            var (expanded, stored) = sizePairs[index];
            var method = LarianCompression.Method(flags);
            var raw = stored == 0 || method == LarianCompressionMethod.None;
            var length = raw ? expanded : stored;
            var source = BinaryData.Slice(bytes, position, length);
            position += length;
            if (length == 0)
            {
                BinaryData.Require(expanded == 0, "Empty LSOF section has a nonzero expanded size.");
                return [];
            }

            var framed = allowFrame && method == LarianCompressionMethod.Lz4 && LarianCompression.IsLz4Frame(source);
            return LarianCompression.Decode(source, expanded, raw ? (byte)0 : flags, framed);
        }
    }
}
