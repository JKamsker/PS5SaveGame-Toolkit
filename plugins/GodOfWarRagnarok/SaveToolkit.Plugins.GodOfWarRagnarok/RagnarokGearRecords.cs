using System.Buffers.Binary;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal sealed record RagnarokGearRecord(ulong Id, uint Instance, uint Parent, uint SlotCount, int Offset);

/// <summary>Optional, read-only framing inference. Unrecognized or ambiguous data stays opaque.</summary>
internal sealed record RagnarokGearRecords(int Offset, int End, int RecordsEnd, IReadOnlyList<RagnarokGearRecord> Records)
{
    internal const int HeaderSize = sizeof(ulong) + 3 * sizeof(uint);
    internal const int SlotSize = 6;
    internal static RagnarokGearRecords Empty { get; } = new(0, 0, 0, []);

    public static RagnarokGearRecords Find(ReadOnlySpan<byte> bytes, RagnarokRecordIndex index)
    {
        if (index.Nested.Wallets.Count == 0) return Empty;
        var end = index.Nested.WalletBlockOffset;
        var directoryEnd = index.SecondaryOffset + index.SecondaryCount * sizeof(ulong);
        var start = Math.Max(directoryEnd, end - ushort.MaxValue - sizeof(ushort));
        RagnarokGearRecords? match = null;
        for (var offset = start; offset <= end - sizeof(ushort) - sizeof(uint) - HeaderSize; offset++)
        {
            var length = BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
            if (offset + sizeof(ushort) + length != end) continue;
            var count = RagnarokFormat.UInt32(bytes, offset + sizeof(ushort));
            if (count == 0 || count > (length - sizeof(uint)) / HeaderSize) continue;
            if (Read(bytes, offset, end, count) is not { } candidate) continue;
            // A second plausible framing defeats identification; never pick the first by accident.
            if (match is not null) return Empty;
            match = candidate;
        }
        return match ?? Empty;
    }

    private static RagnarokGearRecords? Read(ReadOnlySpan<byte> bytes, int offset, int end, uint count)
    {
        var rows = new List<RagnarokGearRecord>((int)count);
        var cursor = offset + sizeof(ushort) + sizeof(uint);
        for (var row = 0; row < count; row++)
        {
            if (cursor > end - HeaderSize) return null;
            var id = RagnarokFormat.UInt64(bytes, cursor);
            var instance = RagnarokFormat.UInt32(bytes, cursor + sizeof(ulong));
            var parent = RagnarokFormat.UInt32(bytes, cursor + sizeof(ulong) + sizeof(uint));
            var slots = RagnarokFormat.UInt32(bytes, cursor + sizeof(ulong) + 2 * sizeof(uint));
            if (id == 0 || instance >= RagnarokFormat.MaximumRecordCount ||
                (row == 0 ? instance != 0 : instance <= rows[^1].Instance) ||
                slots > (end - cursor - HeaderSize) / SlotSize) return null;
            rows.Add(new(id, instance, parent, slots, cursor));
            cursor += HeaderSize + (int)slots * SlotSize;
        }
        var instances = rows.Select(row => row.Instance).ToHashSet();
        if (rows.Any(row => row.Parent != uint.MaxValue && !instances.Contains(row.Parent))) return null;
        // The remainder contains more arrays, but their equip/stat semantics are not established.
        return new(offset, end, cursor, rows);
    }
}
