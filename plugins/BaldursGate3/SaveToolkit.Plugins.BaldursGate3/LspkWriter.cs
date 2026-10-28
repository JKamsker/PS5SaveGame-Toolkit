using System.Buffers.Binary;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Single-part v18 writer; table order and physical write order are retained independently.</summary>
internal static class LspkWriter
{
    private const int PayloadAlignment = 64; // LSLib PackageVersion.PaddingSize(), relative to header end.
    private const byte PaddingByte = 0xad;

    internal static byte[] Write(byte[] original, IReadOnlyDictionary<string, byte[]> replacements)
    {
        var package = LspkPackage.Read(original);
        BinaryData.Require(package.Version == 18, "Writing is supported only for LSPK v18.");
        var expanded = package.Entries.ToDictionary(e => e.Name, package.Extract, StringComparer.Ordinal);
        BinaryData.Require(replacements.Keys.All(expanded.ContainsKey), "Unknown LSPK entry edit.");
        var rowIndices = package.Entries.Select((entry, index) => (entry.Name, index)).ToDictionary(p => p.Name, p => p.index, StringComparer.Ordinal);
        var changed = replacements.Where(pair => !pair.Value.AsSpan().SequenceEqual(expanded[pair.Key])).ToDictionary(StringComparer.Ordinal);
        if (changed.Count == 0)
        {
            return original.ToArray();
        }

        BinaryData.Require(expanded.Sum(pair => (long)(changed.TryGetValue(pair.Key, out var data) ? data.Length : pair.Value.Length)) <= BinaryData.MaxBufferSize,
            "Edited LSPK expanded payloads exceed 256 MiB.");
        var listOffset = BinaryData.Size(BinaryData.U64(original, LspkLayout.FileListOffset));
        var listSize = BinaryData.Size(BinaryData.U32(original, LspkLayout.FileListSize));
        var table = LarianCompression.Decode(original.AsSpan(listOffset + LspkLayout.FileListPreambleSize, listSize - LspkLayout.FileListPreambleSize),
            package.Entries.Count * LspkLayout.Entry18Size, (byte)LarianCompressionMethod.Lz4);
        using var output = new MemoryStream();
        output.Write(original.AsSpan(0, LspkLayout.Header16Size));
        var previousEnd = LspkLayout.Header16Size;
        foreach (var entry in package.Entries.OrderBy(e => e.Offset))
        {
            // Unknown gaps and padding are retained, even if additional alignment bytes are needed.
            output.Write(original.AsSpan(previousEnd, entry.Offset - previousEnd));
            Align(output);
            var rowIndex = rowIndices[entry.Name];
            var row = table.AsSpan(rowIndex * LspkLayout.Entry18Size, LspkLayout.Entry18Size);
            BinaryPrimitives.WriteUInt32LittleEndian(row[LspkLayout.EntryOffset..], checked((uint)output.Position));
            BinaryPrimitives.WriteUInt16LittleEndian(row[LspkLayout.Entry18OffsetHigh..], 0);
            if (changed.TryGetValue(entry.Name, out var data))
            {
                var compressed = LarianCompression.Encode(data, entry.Flags);
                BinaryPrimitives.WriteUInt32LittleEndian(row[LspkLayout.EntryStoredSize..], (uint)compressed.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(row[LspkLayout.Entry18ExpandedSize..], entry.Compression == LarianCompressionMethod.None ? 0u : (uint)data.Length);
                output.Write(compressed);
                expanded[entry.Name] = data;
            }
            else
            {
                output.Write(original.AsSpan(entry.Offset, entry.StoredSize));
            }

            previousEnd = entry.Offset + entry.StoredSize;
            BinaryData.Size((ulong)output.Length);
        }

        output.Write(original.AsSpan(previousEnd, listOffset - previousEnd));
        Align(output);
        var newListOffset = output.Position;
        var compressedTable = LarianCompression.Encode(table, (byte)LarianCompressionMethod.Lz4);
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, true))
        {
            writer.Write(package.Entries.Count);
            writer.Write(compressedTable.Length);
            writer.Write(compressedTable);
        }

        var newListSize = output.Position - newListOffset;
        output.Write(original.AsSpan(listOffset + listSize));
        BinaryData.Size((ulong)output.Length);
        var result = output.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(LspkLayout.FileListOffset), (ulong)newListOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(LspkLayout.FileListSize), (uint)newListSize);
        LspkIntegrity.Compute(package.Entries.OrderBy(e => e.Offset).Select(e => expanded[e.Name])).CopyTo(result, LspkLayout.Digest);
        var reopened = LspkPackage.Read(result); // validates the new digest as well as all ranges/codecs
        for (var i = 0; i < package.Entries.Count; i++)
        {
            var entry = reopened.Entries[i];
            BinaryData.Require(entry.Name == package.Entries[i].Name && entry.Flags == package.Entries[i].Flags &&
                reopened.Extract(entry).AsSpan().SequenceEqual(expanded[entry.Name]), "LSPK edit did not survive writer validation.");
        }

        return result;
    }

    private static void Align(MemoryStream output)
    {
        while ((output.Position - LspkLayout.Header16Size) % PayloadAlignment != 0)
        {
            output.WriteByte(PaddingByte);
        }
    }
}
