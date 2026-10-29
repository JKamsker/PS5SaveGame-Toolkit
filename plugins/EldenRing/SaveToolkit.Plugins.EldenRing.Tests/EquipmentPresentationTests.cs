using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class EquipmentPresentationTests
{
    // Independent presentation contract, including the intentionally unnamed words.
    private static readonly string?[] GearLabels =
    [
        "Left hand 1", "Right hand 1", "Left hand 2", "Right hand 2", "Left hand 3", "Right hand 3",
        "Arrow 1", "Bolt 1", "Arrow 2", "Bolt 2", null, null,
        "Head", "Chest", "Arms", "Legs", null, "Talisman 1", "Talisman 2", "Talisman 3", "Talisman 4", null,
    ];

    [Fact]
    public void RelationshipRowsKeepEveryKnownGearAndQuickSlotInNativeOrder()
    {
        var fixture = new SemanticFixture();
        var slot = EldenRingParser.Parse(fixture.Save.Bytes).Slots[0];
        var links = EquipmentReader.Read(fixture.Save.Bytes, SemanticReader.Read(fixture.Save.Bytes, slot));
        var expected = GearLabels.Select((label, word) => (Key: $"gear/{word}", Label: label))
            .Where(item => item.Label is not null)
            .Concat(Enumerable.Range(0, 10).Select(index => (Key: $"quick/{index}", Label: (string?)$"Quick item {index + 1}")))
            .Concat(Enumerable.Range(0, 6).Select(index => (Key: $"pouch/{index}", Label: (string?)$"Pouch {index + 1}")));
        Assert.Equal(expected, links.Select(link => (link.Key, (string?)link.Label)));
    }

    [Theory]
    [InlineData("EquipData", 22, 0)]
    [InlineData("ChrAsm", 29, 7)]
    [InlineData("ChrAsm2", 22, 0)]
    [InlineData("EquippedItems", 39, 0)]
    public void RawRowsKeepSelectorsKnownLabelsAndUnknownWordFallbacks(string range, int words, int prefix)
    {
        var fixture = new SemanticFixture();
        using var document = fixture.Save.Open();
        var section = Assert.Single(document.ExpandNode("characters/0").OfType<SectionNode>(),
            section => section.Title == "Equipment raw representations");
        var table = Assert.Single(section.Children.OfType<TableNode>(), table => table.Title == range);
        Assert.Equal(words, table.Rows.Count);
        string[] selectors = ["Arm style", "Active left hand", "Active right hand", "Active projectile selector 1",
            "Active projectile selector 2", "Active projectile selector 3", "Active projectile selector 4"];
        for (var word = 0; word < words; word++)
        {
            var gear = word - prefix;
            var expected = word < prefix ? selectors[word]
                : gear < GearLabels.Length && GearLabels[gear] is { } label ? label : $"Unknown word +0x{word * sizeof(uint):X}";
            var row = table.Rows[word];
            var field = Assert.IsType<FieldNode>(row.Cells["raw"]);
            Assert.Equal($"slots/0/equipment/{range}/{word}", row.Key);
            Assert.Equal(row.Key, field.ValueId);
            Assert.Equal(expected, Assert.IsType<TextNode>(row.Cells["slot"]).Text);
            Assert.Equal(expected, field.Label);
            Assert.Equal(Confidence.Inferred, field.Confidence);
            Assert.True(field.Spec.ReadOnly);
            Assert.Equal(field.Value, document.GetValue(field.ValueId));
            Assert.Equal(FieldValue.Of(0L), field.Value);
        }
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
    }
}
