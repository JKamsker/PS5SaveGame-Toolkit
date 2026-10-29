using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class ProfileEquipmentTests
{
    [Theory]
    [InlineData(0, 0x17E, 0x1D6, 0x80010001U, 0x00001000U)]
    [InlineData(12, 0x1AE, 0x206, 0x90010002U, 0x10001000U)]
    [InlineData(17, 0x1C2, 0x21A, 0xA00003E8U, 0x200003E8U)]
    public void SyntheticProfileCopiesHaveIndependentOffsetsAndRemainReadOnly(int word, int handleOffset,
        int itemOffset, uint handle, uint item)
    {
        var fixture = new SemanticFixture();
        var slot = EldenRingParser.Parse(fixture.Save.Bytes).Slots[0];
        var character = SemanticReader.Read(fixture.Save.Bytes, slot);
        if (word == 0) fixture.Instance(1, handle, item);
        if (word == 12) fixture.Instance(2, handle, item);
        fixture.Row("held", "common", 9, handle, 1, 777);
        var ranges = character.Ranges.ToDictionary(range => range.Name);
        fixture.Save.Write32(ranges["EquipData"].Offset + word * sizeof(uint), 0x189);
        fixture.Save.Write32(ranges["ChrAsm2"].Offset + word * sizeof(uint), handle);
        fixture.Save.Write32(ranges["ChrAsm"].Offset + (word + 7) * sizeof(uint), item & 0x0FFFFFFF);
        fixture.Save.Write32(ranges["EquippedItems"].Offset + word * sizeof(uint), item);
        var encoded = word == 0 ? 0x80000000U | (item & 0x007FFFFF) : 0x80000000U | item;
        fixture.Save.Write32(slot.ProfileOffset + handleOffset, encoded);
        fixture.Save.Write32(slot.ProfileOffset + itemOffset, item & 0x0FFFFFFF);
        var before = fixture.Save.Bytes.ToArray();
        using var document = fixture.Save.Open();
        var table = Assert.Single(document.ExpandNode("characters/0").OfType<TableNode>(),
            table => table.Title == "Existing equipment relationships (read-only)");
        var row = Assert.Single(table.Rows, row => row.Key.EndsWith($"/gear/{word}", StringComparison.Ordinal));
        var handleField = Assert.IsType<FieldNode>(row.Cells["profile-encoded"]);
        var itemField = Assert.IsType<FieldNode>(row.Cells["profile-item"]);
        Assert.Equal(encoded, handleField.Value.AsUInt32());
        Assert.Equal(item & 0x0FFFFFFF, itemField.Value.AsUInt32());
        Assert.All(new[] { handleField, itemField }, field =>
        {
            Assert.True(field.Spec.ReadOnly);
            Assert.Equal(Confidence.Inferred, field.Confidence);
            Assert.False(document.ApplyEdits([new(field.ValueId, SaveToolkit.Sdk.Values.FieldValue.Of(1L))]).Succeeded);
        });
        Assert.Contains("agree", Assert.IsType<TextNode>(row.Cells["profile-status"]).Text);
        WriteTestSupport.EqualBytes(before, document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SampleProfileCopiesAreComparedForEveryAssignedGearSlot(bool backup)
    {
        var file = Assert.Single(SaveToolkit.Testing.SampleSaves.Load("PPSA03169").AllDiskFiles,
            file => file.FileName == "memory.dat" && file.Directory.IsBackup == backup);
        var bytes = file.ReadAllBytes();
        var slot = EldenRingParser.Parse(bytes).Slots[0];
        var character = SemanticReader.Read(bytes, slot);
        foreach (var link in EquipmentReader.Read(bytes, character).Where(link => link.Key.StartsWith("gear/", StringComparison.Ordinal)))
        {
            var word = int.Parse(link.Key.AsSpan(5), System.Globalization.CultureInfo.InvariantCulture);
            var encoded = EldenRingParser.ReadUInt32(bytes, slot.ProfileOffset + 0x17E + word * sizeof(uint));
            var item = EldenRingParser.ReadUInt32(bytes, slot.ProfileOffset + 0x1D6 + word * sizeof(uint));
            if (link.Entry is null)
            {
                Assert.Equal(0U, encoded);
                Assert.Equal(uint.MaxValue, item);
                continue; // Observed sentinels, not universal empty/unarmed rules.
            }
            var expected = word < 10 ? (link.EquippedId & 0x007FFFFF) | 0x80000000 : link.EquippedId | 0x80000000;
            Assert.Equal(expected, encoded);
            Assert.Equal(link.EquippedId & 0x0FFFFFFF, item);
        }
    }

    [Theory]
    [InlineData(90U, true)]
    [InlineData(91U, false)]
    public void LastSlotUsesItsOwnProfileAndUnsupportedVersionsOmitCandidates(uint version, bool modeled)
    {
        var fixture = new SyntheticSave([9]);
        fixture.Write32(SyntheticSave.UserData, version);
        fixture.Write32(fixture.Profile(9) + 0x17E, 123);
        using var document = fixture.Open();
        var table = document.ExpandNode("characters/9").OfType<TableNode>()
            .Single(table => table.Title == "Existing equipment relationships (read-only)");
        var row = table.Rows.Single(row => row.Key == "slots/9/equipment-links/gear/0");
        if (modeled)
            Assert.Equal(123U, Assert.IsType<FieldNode>(row.Cells["profile-encoded"]).Value.AsUInt32());
        else
            Assert.Equal("Not modeled", Assert.IsType<TextNode>(row.Cells["profile-encoded"]).Text);
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
    }

    [Theory]
    [InlineData(0x17E)]
    [InlineData(0x1D6)]
    public void ProfileDisagreementIsReportedAndNeverRepaired(int offset)
    {
        var fixture = new SemanticFixture();
        fixture.Instance(1, 0x80010001, 0x00001000);
        fixture.Row("held", "common", 9, 0x80010001, 1, 777);
        var slot = EldenRingParser.Parse(fixture.Save.Bytes).Slots[0];
        var ranges = SemanticReader.Read(fixture.Save.Bytes, slot).Ranges.ToDictionary(range => range.Name);
        fixture.Save.Write32(ranges["ChrAsm2"].Offset, 0x80010001);
        fixture.Save.Write32(fixture.Save.Profile(0) + offset, 123);
        var before = fixture.Save.Bytes.ToArray();
        using var document = fixture.Save.Open();
        var table = document.ExpandNode("characters/0").OfType<TableNode>()
            .Single(table => table.Title == "Existing equipment relationships (read-only)");
        var row = table.Rows.Single(row => row.Key.EndsWith("/gear/0", StringComparison.Ordinal));
        Assert.Contains("disagreement", Assert.IsType<TextNode>(row.Cells["profile-status"]).Text);
        WriteTestSupport.EqualBytes(before, document);
    }
}
