namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Retains original section encodings, header and unreferenced trailing bytes.</summary>
internal sealed class LsfBinary
{
    private readonly byte[] _original;
    private readonly int _headerSize;
    private readonly int _end;
    private readonly byte _flags;
    private readonly int[] _order;
    private readonly Section[] _sections;

    private LsfBinary(byte[] bytes, int headerSize, int end, byte flags, int[] order, Section[] sections)
    {
        _original = bytes;
        _headerSize = headerSize;
        _end = end;
        _flags = flags;
        _order = order;
        _sections = sections;
    }

    internal uint Version => BinaryData.U32(_original, LsfLayout.Version);
    internal uint Metadata => BinaryData.U32(_original, _headerSize - sizeof(uint));
    internal int Pair(int v6Pair) => v6Pair == LsfLayout.StringsPair || Version >= 6 ? v6Pair : v6Pair - 1;
    internal byte[] Data(int v6Pair) => _sections[Pair(v6Pair)].Expanded;

    internal static LsfBinary Read(byte[] bytes, ResourceBudget? budget = null)
    {
        BinaryData.Require(bytes.Length >= LsfLayout.MetadataOffset && bytes.AsSpan().StartsWith("LSOF"u8), "Not an LSOF resource.");
        var version = BinaryData.U32(bytes, LsfLayout.Version);
        BinaryData.Require(version is >= 5 and <= 7, $"Unsupported LSOF version {version}; expected 5, 6 or 7.");
        var pairs = version >= 6 ? LsfLayout.V6PairCount : LsfLayout.V5PairCount;
        var flagsOffset = LsfLayout.MetadataOffset + pairs * LsfLayout.SizePairLength;
        var headerSize = flagsOffset + LsfLayout.MetadataTrailerSize;
        BinaryData.Slice(bytes, 0, headerSize);
        var flags = bytes[flagsOffset];
        var metadata = BinaryData.U32(bytes, flagsOffset + LsfLayout.MetadataFormatOffset);
        BinaryData.Require(metadata <= 2, "Unsupported LSOF metadata format.");
        var method = LarianCompression.Method(flags);
        var sizes = new (int Expanded, int Stored)[pairs];
        long total = 0;
        for (var i = 0; i < pairs; i++)
        {
            var offset = LsfLayout.MetadataOffset + i * LsfLayout.SizePairLength;
            sizes[i] = (BinaryData.Size(BinaryData.U32(bytes, offset)), BinaryData.Size(BinaryData.U32(bytes, offset + sizeof(uint))));
            total += sizes[i].Expanded;
        }

        BinaryData.Require(total <= BinaryData.MaxBufferSize, "LSOF expanded sections exceed 256 MiB.");
        var shift = version >= 6 ? 0 : 1;
        var recordSize = metadata == 1 ? LsfLayout.AdjacencyRecordSize : LsfLayout.CompactRecordSize;
        ValidateTableSize(sizes[LsfLayout.NodesPair - shift].Expanded, recordSize);
        ValidateTableSize(sizes[LsfLayout.AttributesPair - shift].Expanded, recordSize);
        if (version >= 6) ValidateTableSize(sizes[LsfLayout.KeysPair].Expanded, LsfLayout.KeyRecordSize);
        int[] order = version >= 6 ? [0, 2, 3, 4, 1] : [0, 1, 2, 3];
        var end = headerSize;
        foreach (var index in order)
        {
            var (expanded, stored) = sizes[index];
            BinaryData.Require(method != LarianCompressionMethod.None || stored == 0 || stored == expanded,
                "Uncompressed LSOF section has an inconsistent stored size.");
            var length = stored == 0 || method == LarianCompressionMethod.None ? expanded : stored;
            BinaryData.Slice(bytes, end, length);
            end += length;
        }

        (budget ?? new ResourceBudget()).Reserve(total, sizes[LsfLayout.NodesPair - shift].Expanded / recordSize,
            sizes[LsfLayout.AttributesPair - shift].Expanded / recordSize);
        var (sections, position) = ReadSections(bytes, headerSize, flags, method, sizes, order);
        return new LsfBinary(bytes, headerSize, position, flags, order, sections);
    }

    private static (Section[] Sections, int End) ReadSections(byte[] bytes, int headerSize, byte flags,
        LarianCompressionMethod method, (int Expanded, int Stored)[] sizes, int[] order)
    {
        var sections = new Section[sizes.Length];
        var position = headerSize;
        foreach (var index in order)
        {
            var (expanded, stored) = sizes[index];
            var raw = stored == 0 || method == LarianCompressionMethod.None;
            var length = raw ? expanded : stored;
            var source = BinaryData.Slice(bytes, position, length);
            var framed = index != 0 && method == LarianCompressionMethod.Lz4 && LarianCompression.IsLz4Frame(source);
            var data = length == 0 ? [] : LarianCompression.Decode(source, expanded, raw ? (byte)0 : flags, framed);
            BinaryData.Require(length != 0 || expanded == 0, "Empty LSOF section has a nonzero expanded size.");
            sections[index] = new Section(position, length, stored, framed, data);
            position += length;
        }

        return (sections, position);
    }

    private static void ValidateTableSize(int size, int recordSize) =>
        BinaryData.Require(size % recordSize == 0 && size / recordSize <= BinaryData.MaxRecords,
            "Invalid LSOF table size or excessive record count (one million limit).");

    internal byte[] Rebuild(IReadOnlyDictionary<int, byte[]> replacements)
    {
        BinaryData.Require(replacements.Keys.All(index => index >= 0 && index < _sections.Length), "Unknown LSOF section edit.");
        var expandedSize = _sections.Select((section, index) => (long)(replacements.TryGetValue(index, out var data) ? data.Length : section.Expanded.Length)).Sum();
        BinaryData.Require(expandedSize <= BinaryData.MaxBufferSize, "Edited LSOF expanded sections exceed 256 MiB.");
        var header = _original[.._headerSize];
        using var output = new MemoryStream();
        output.Write(header);
        foreach (var index in _order)
        {
            var section = _sections[index];
            if (!replacements.TryGetValue(index, out var data) || data.AsSpan().SequenceEqual(section.Expanded))
            {
                output.Write(_original.AsSpan(section.Offset, section.Length));
                continue;
            }

            var raw = section.StoredSize == 0 || LarianCompression.Method(_flags) == LarianCompressionMethod.None;
            var stored = raw ? data : LarianCompression.Encode(data, _flags, section.Framed);
            output.Write(stored);
            BinaryData.Size((ulong)output.Length);
            var offset = LsfLayout.MetadataOffset + index * LsfLayout.SizePairLength;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(offset), (uint)data.Length);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(offset + sizeof(uint)), raw ? 0u : (uint)stored.Length);
        }

        output.Write(_original.AsSpan(_end));
        BinaryData.Size((ulong)output.Length);
        output.Position = 0;
        output.Write(header);
        return output.ToArray();
    }

    private sealed record Section(int Offset, int Length, int StoredSize, bool Framed, byte[] Expanded);
}
