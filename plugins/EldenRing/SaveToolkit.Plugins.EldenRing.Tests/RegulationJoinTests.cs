using System.Buffers.Binary;
using System.Text;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class RegulationJoinTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SampleEquipmentAndLarvalTearHaveExactRegulationRowIds(bool backup)
    {
        var file = Assert.Single(SaveToolkit.Testing.SampleSaves.Load("PPSA03169").AllDiskFiles,
            file => file.FileName == "memory.dat" && file.Directory.IsBackup == backup);
        var bytes = file.ReadAllBytes();
        var decoded = RegulationDecoder.Decode(bytes.AsSpan(EldenRingLayout.RegulationOffset, EldenRingLayout.RegulationSize));
        _ = RegulationArchiveReader.Read(decoded); // Validate all framing/directories before the research-only row join.
        var rows = new Dictionary<string, HashSet<uint>>(StringComparer.Ordinal)
        {
            ["weapon"] = Rows(decoded, "EquipParamWeapon.param"),
            ["armor"] = Rows(decoded, "EquipParamProtector.param"),
            ["talisman"] = Rows(decoded, "EquipParamAccessory.param"),
            ["goods"] = Rows(decoded, "EquipParamGoods.param"),
        };
        var slot = Assert.Single(EldenRingParser.Parse(bytes).Slots, slot => slot.Active);
        var character = SemanticReader.Read(bytes, slot);
        foreach (var link in EquipmentReader.Read(bytes, character).Where(link => link.Entry?.ItemId is not null))
        {
            var entry = link.Entry!;
            // PARAM keys exclude the category nibble; retain the exact weapon affinity/upgrade ID.
            Assert.Contains(entry.ItemId!.Value & 0x0FFFFFFF, rows[entry.Category]);
            if (entry.Category is "weapon" or "armor")
                Assert.Contains(character.Instances, instance => instance.Handle == link.Handle && instance.ItemId == entry.ItemId);
        }
        Assert.Contains(8185U, rows["goods"]); // Larval Tear is catalog/regulation evidence, not sample ownership.
    }

    // This independently reads only numeric row IDs from the already validated sample schema.
    // No PARAM payload values, weight, legal caps or downloaded implementation are imported.
    private static HashSet<uint> Rows(byte[] binder, string table)
    {
        const int binderHeader = 0x40;
        const int binderEntry = 0x24;
        const int paramHeader = 0x40;
        const int paramRow = 0x18;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(binder.AsSpan(0x0C));
        for (var index = 0; index < count; index++)
        {
            var entry = binderHeader + index * binderEntry;
            var nameOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(binder.AsSpan(entry + 32)));
            var end = nameOffset;
            while (binder[end] != 0 || binder[end + 1] != 0) end += sizeof(ushort);
            var name = Encoding.Unicode.GetString(binder, nameOffset, end - nameOffset);
            if (!name.EndsWith("\\" + table, StringComparison.Ordinal)) continue;
            var payload = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(binder.AsSpan(entry + 24)));
            var rowCount = BinaryPrimitives.ReadUInt16LittleEndian(binder.AsSpan(payload + 10));
            return Enumerable.Range(0, rowCount).Select(row =>
                BinaryPrimitives.ReadUInt32LittleEndian(binder.AsSpan(payload + paramHeader + row * paramRow))).ToHashSet();
        }
        Assert.Fail("Required regulation table absent: " + table);
        return [];
    }
}
