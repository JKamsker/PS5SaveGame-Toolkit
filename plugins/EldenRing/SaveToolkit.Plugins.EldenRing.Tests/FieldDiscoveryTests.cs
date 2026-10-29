using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class FieldDiscoveryTests
{
    private static readonly string[] Attributes =
        ["vigor", "mind", "endurance", "strength", "dexterity", "intelligence", "faith", "arcane"];

    [Fact]
    public void SyntheticAttributesAndLevelAreInferredReadOnlyAndDecodedWithoutSecrets()
    {
        var fixture = new SemanticFixture();
        uint[] values = [20, 15, 14, 13, 12, 11, 10, 9];
        for (var index = 0; index < values.Length; index++)
            fixture.Save.Write32(fixture.Player + SyntheticSave.Attributes + index * sizeof(uint), values[index]);
        const uint level = 25; // Independent sum: 104 - 79.
        fixture.Save.Write32(fixture.Player + SyntheticSave.Level, level);
        fixture.Save.Write32(fixture.Save.Profile(0) + SyntheticSave.ProfileLevel, level);
        // A known goods handle is sufficient; it is not a GaItem instance-map entry.
        fixture.Row("held", "key", 3, 0xB0001FF9, 2, 71);
        var before = fixture.Save.Bytes.ToArray();
        using var document = fixture.Save.Open();
        var fields = document.ExpandNode("characters/0").OfType<FieldNode>().ToArray();
        var tree = new HookSaveDecoder(new EldenRingEvidenceDecoder()).Decode(fixture.Save.File(), new());
        for (var index = 0; index < Attributes.Length; index++)
        {
            var id = "slots/0/attributes/" + Attributes[index];
            var field = Assert.Single(fields, field => field.ValueId == id);
            Assert.Equal(values[index], field.Value.AsUInt32());
            Assert.True(field.Spec.ReadOnly);
            Assert.Equal(Confidence.Inferred, field.Confidence);
            Assert.Contains("Source:", field.Description);
            Assert.False(document.ApplyEdits([new(id, FieldValue.Of(values[index] + 1L))]).Succeeded);
            Assert.Contains(tree.Values, value => value.Path == id && value.Value == values[index].ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        var levelField = Assert.Single(fields, field => field.ValueId == "slots/0/level");
        Assert.Equal(Confidence.Inferred, levelField.Confidence);
        Assert.True(levelField.Spec.ReadOnly);
        Assert.Contains(tree.Values, value => value.Path == "slots/0/level" && value.Value == "25");
        Assert.Contains(tree.Values, value => value.Path == "slots/0/profile-level" && value.Value == "25");
        Assert.Contains(tree.Values, value => value.Path.EndsWith("/identity", StringComparison.Ordinal) && value.Value == "0x40001FF9");
        var inventory = ((SaveToolkit.Sdk.Collections.IQueryableDocument)document).Query("slots/0/inventory/held", new() { Text = "0x40001FF9" });
        var quantity = Assert.IsType<FieldNode>(Assert.Single(inventory.Rows).Cells["quantity"]);
        Assert.Equal(2U, quantity.Value.AsUInt32());
        Assert.True(quantity.Spec.ReadOnly);
        Assert.False(document.ApplyEdits([new(quantity.ValueId, FieldValue.Of(3L))]).Succeeded);
        Assert.DoesNotContain(tree.Values, value => value.Value.Contains("Hero", StringComparison.Ordinal));
        WriteTestSupport.EqualBytes(before, document);
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(100U)]
    [InlineData(11U)] // In-range but level/profile no longer match the sum.
    public void SyntheticOutOfBoundsOrInconsistentAttributesCannotPublishEvidence(uint value)
    {
        var fixture = new SemanticFixture();
        fixture.Save.Write32(fixture.Player + SyntheticSave.Attributes, value);
        var decoder = new HookSaveDecoder(new EldenRingEvidenceDecoder());
        Assert.Throws<SaveToolkit.Sdk.SaveFormatException>(() => decoder.Decode(fixture.Save.File(), new()));
    }
}
