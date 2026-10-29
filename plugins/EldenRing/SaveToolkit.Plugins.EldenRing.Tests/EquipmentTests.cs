using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class EquipmentTests
{
    [Fact]
    public void SampleGearQuickAndPouchReferencesAgreeWithoutConflatingAcquisitionIndex()
    {
        var file = WriteTestSupport.Sample();
        var bytes = file.ReadAllBytes();
        var slot = EldenRingParser.Parse(bytes).Slots[0];
        var character = SemanticReader.Read(bytes, slot);
        var links = EquipmentReader.Read(bytes, character);
        Assert.Equal(34, links.Count);
        Assert.Equal(17, links.Count(link => link.Status.StartsWith("Representations agree", StringComparison.Ordinal)));
        Assert.DoesNotContain(links, link => link.Status.Contains("disagreement", StringComparison.Ordinal));
        var left = links.Single(link => link.Label == "Left hand 1");
        var right = links.Single(link => link.Label == "Right hand 1");
        Assert.Equal(0x006CF2A0U, left.Entry!.ItemId);
        Assert.Equal(left.Entry.ItemId, right.Entry!.ItemId);
        Assert.NotEqual(left.Handle, right.Handle);
        Assert.NotEqual(left.Entry.Key(0), right.Entry.Key(0));
        Assert.Equal(0x182U, left.Position);
        Assert.Equal(2, left.Entry.Record);
        Assert.Equal(0x24DU, left.Entry.InventoryIndex);
        Assert.Equal(0x200003E8U, links.Single(link => link.Label == "Talisman 1").Entry!.ItemId);
        Assert.Equal(0x3E8U, links.Single(link => link.Label == "Talisman 1").AssemblyId);
        Assert.Equal(0x400003E9U, links.Single(link => link.Label == "Quick item 1").EquippedId);
        Assert.Equal(0x40000073U, links.Single(link => link.Label == "Pouch 1").EquippedId);

        var plugin = new EldenRingPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA03169");
        using var document = plugin.Open(file, context);
        var table = document.ExpandNode("characters/0").OfType<TableNode>()
            .Single(table => table.Title == "Existing equipment relationships (read-only)");
        Assert.Equal(34, table.Rows.Count);
        var head = table.Rows.Single(row => row.Key.EndsWith("gear/12", StringComparison.Ordinal));
        var identity = Assert.IsType<FieldNode>(head.Cells["item"]);
        Assert.Equal("Blue Cloth Cowl", identity.Label);
        Assert.Equal("0x100A3930", identity.Value.AsString());
        Assert.Equal(ReferenceBrowser.CatalogId, Assert.IsType<ReferenceSpec>(identity.Spec).CatalogId);
        Assert.True(identity.Spec.ReadOnly);
        Assert.Equal(identity.Value, document.GetValue(identity.ValueId));
        Assert.False(document.ApplyEdits([new(identity.ValueId, FieldValue.Of("0x006CF2A0"))]).Succeeded);
        var provenance = Assert.IsType<SectionNode>(head.Cells["source"]);
        Assert.Contains(provenance.Children, node => node is TextNode text && text.Text.Contains("MIT", StringComparison.Ordinal));
        Assert.True(bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Theory]
    [InlineData("EquipData")]
    [InlineData("ChrAsm")]
    [InlineData("EquippedItems")]
    public void DisagreementIsVisibleAndNeverRepaired(string representation)
    {
        var fixture = LinkedWeapon();
        var character = Read(fixture);
        var range = character.Ranges.Single(range => range.Name == representation);
        fixture.Save.Write32(range.Offset + (representation == "ChrAsm" ? 7 * sizeof(uint) : 0), 123);
        var before = fixture.Save.Bytes.ToArray();
        var link = EquipmentReader.Read(fixture.Save.Bytes, Read(fixture)).Single(link => link.Key == "gear/0");
        Assert.Contains("disagreement", link.Status);
        Assert.NotNull(link.Entry);
        using var document = fixture.Save.Open();
        _ = document.ExpandNode("characters/0");
        Assert.True(before.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("zero-quantity")]
    [InlineData("wrong-category")]
    public void AmbiguousMissingOrInvalidHeldRowsStayProtected(string condition)
    {
        var fixture = LinkedWeapon();
        var character = Read(fixture);
        switch (condition)
        {
            case "duplicate": fixture.Row("held", "common", 4, 0x80010001, 1, 99); break;
            case "missing": fixture.Save.Write32(character.Ranges.Single(range => range.Name == "ChrAsm2").Offset, 0x8001FFFF); break;
            case "zero-quantity": fixture.Save.Write32(fixture.Held + 4 + 3 * 12 + 4, 0); break;
            case "wrong-category": fixture.Instance(1, 0x80010001, 0x10001000); break;
        }
        var link = EquipmentReader.Read(fixture.Save.Bytes, Read(fixture)).Single(link => link.Key == "gear/0");
        Assert.DoesNotContain("Representations agree", link.Status);
        if (condition == "duplicate") Assert.Null(link.Entry);
    }

    private static SemanticFixture LinkedWeapon()
    {
        var fixture = new SemanticFixture();
        fixture.Instance(1, 0x80010001, 0x006CF2A0);
        fixture.Row("held", "common", 3, 0x80010001, 1, 999); // deliberately unrelated acquisition index
        var ranges = Read(fixture).Ranges.ToDictionary(range => range.Name);
        fixture.Save.Write32(ranges["EquipData"].Offset, 0x183);
        fixture.Save.Write32(ranges["ChrAsm"].Offset + 7 * sizeof(uint), 0x006CF2A0);
        fixture.Save.Write32(ranges["ChrAsm2"].Offset, 0x80010001);
        fixture.Save.Write32(ranges["EquippedItems"].Offset, 0x006CF2A0);
        Assert.Contains("Representations agree", EquipmentReader.Read(fixture.Save.Bytes, Read(fixture)).Single(link => link.Key == "gear/0").Status);
        return fixture;
    }

    private static SemanticCharacter Read(SemanticFixture fixture) =>
        SemanticReader.Read(fixture.Save.Bytes, EldenRingParser.Parse(fixture.Save.Bytes).Slots[0]);
}
