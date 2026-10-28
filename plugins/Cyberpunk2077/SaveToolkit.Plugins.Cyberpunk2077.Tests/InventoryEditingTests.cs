using System.Buffers.Binary;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class InventoryEditingTests
{
    [Theory]
    [InlineData(44, "Items.FirstAidWhiffV0", 1U)]
    [InlineData(45, "Items.BonesMcCoy70V0", 1U)]
    [InlineData(49, "Ammo.HandgunAmmo", 600U)]
    [InlineData(50, "Ammo.RifleAmmo", 600U)]
    [InlineData(51, "Ammo.ShotgunAmmo", 120U)]
    public void EveryVerifiedPlayerStackIsNamedEditableAndChangesOnlyItsQuantity(int node, string name, uint quantity)
    {
        foreach (var file in ProgressionSampleTests.Files())
        {
            var before = CsavContainer.Parse(file.ReadAllBytes());
            var tree = CsavTree.Parse(before);
            var item = Assert.Single(InventoryReader.Read(before, tree), i => i.NodeId == node);
            Assert.Equal(ItemNames.TweakHash(name), item.TweakId);
            Assert.Equal(quantity, BinaryData.UInt32(before.Payload, item.QuantityOffset!.Value));
            using var document = new Cyberpunk2077Plugin().Open(file);
            var fields = CyberpunkSampleTests.ExpandAll(document);
            var field = Assert.Single(fields, f => f.ValueId == $"inventory/{node}/quantity");
            Assert.False(field.Spec.ReadOnly);
            var table = Assert.IsType<TableNode>(document.ExpandNode("inventory/0/0")[0]);
            Assert.Equal(name, Assert.IsType<FieldNode>(Assert.Single(table.Rows, r => r.Key == node.ToString()).Cells["id"]).Value.AsString());
            Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(456L))]).Succeeded);
            var bytes = Assert.Single(document.Serialize()).Content.ToArray();
            var after = CsavContainer.Parse(bytes);
            var expected = before.Payload.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(item.QuantityOffset.Value), 456);
            Assert.Equal(expected, after.Payload);
            using var reopened = ProgressionSampleTests.Open(bytes);
            _ = CyberpunkSampleTests.ExpandAll(reopened);
            Assert.Equal(FieldValue.Of(456L), reopened.GetValue(field.ValueId));
            Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
            Assert.Equal(before.Original, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Theory]
    [InlineData("Items.CommonMaterial1", true)]
    [InlineData("Items.LegendaryMaterial1", true)]
    [InlineData("Ammo.SniperRifleAmmo", true)]
    [InlineData("Items.NotInTheDictionary", false)]
    [InlineData("Items.PlayerMaTppHead", false)]
    [InlineData("Ammo.RecipeHandgunAmmo", false)]
    public void DictionaryCategoriesGateSyntheticQuantityOnlyRecords(string name, bool writable)
    {
        var original = CsavContainer.Parse(ProgressionSampleTests.Files()[0].ReadAllBytes());
        var tree = CsavTree.Parse(original);
        var item = tree.Nodes[49];
        var payload = original.Payload.ToArray();
        var hash = ItemNames.TweakHash(name);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(item.Offset - 16), hash);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(item.Offset + 4), hash);
        using var document = ProgressionSampleTests.Open(original.Rebuild(payload));
        var field = Assert.Single(CyberpunkSampleTests.ExpandAll(document), f => f.ValueId == "inventory/49/quantity");
        Assert.Equal(!writable, field.Spec.ReadOnly);
        var result = document.ApplyEdits([new(field.ValueId, FieldValue.Of(999L))]);
        Assert.Equal(writable, result.Succeeded);
        var table = Assert.IsType<TableNode>(document.ExpandNode("inventory/0/0")[0]);
        var label = Assert.IsType<FieldNode>(Assert.Single(table.Rows, r => r.Key == "49").Cells["id"]).Value.AsString();
        Assert.Equal(new ItemNames().Find(hash)?.Name ?? $"0x{hash:X16}", label);
        if (writable)
        {
            using var reopened = ProgressionSampleTests.Open(Assert.Single(document.Serialize()).Content.ToArray());
            _ = CyberpunkSampleTests.ExpandAll(reopened);
            Assert.Equal(FieldValue.Of(999L), reopened.GetValue(field.ValueId));
        }
    }

    [Fact]
    public void OtherContainersAndExtendedItemsRemainReadOnly()
    {
        var original = CsavContainer.Parse(ProgressionSampleTests.Files()[0].ReadAllBytes());
        var tree = CsavTree.Parse(original);
        var inventory = Assert.Single(tree.Nodes, n => n.Name == "inventory");
        var payload = original.Payload.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(inventory.Offset + 8), 2);
        using var other = ProgressionSampleTests.Open(original.Rebuild(payload));
        Assert.All(CyberpunkSampleTests.ExpandAll(other).Where(f => f.ValueId.StartsWith("inventory/", StringComparison.Ordinal)),
            f => Assert.True(f.Spec.ReadOnly));
        using var document = ProgressionSampleTests.Open(original.Original);
        Assert.All(CyberpunkSampleTests.ExpandAll(document).Where(f => f.ValueId.StartsWith("inventory/", StringComparison.Ordinal) &&
            int.TryParse(f.ValueId.Split('/')[1], out var node) && tree.Nodes[node].Size != 29), f => Assert.True(f.Spec.ReadOnly));
    }

    [Fact]
    public void ReferenceHashesMatchIndependentPublishedVectors()
    {
        Assert.Equal(InventoryReader.MoneyTweakId, ItemNames.TweakHash("Items.money"));
        Assert.Equal(0x00000009CBF43926UL, ItemNames.TweakHash("123456789"));
        Assert.Equal(0x82C21ACDBCD6E830UL, PlayerStatsReader.NameHash("Strength"));
        Assert.Equal(0xAA8AA81AD6F055FDUL, PlayerStatsReader.NameHash("Level"));
        Assert.Equal(0xCBF29CE484222325UL, PlayerStatsReader.NameHash(string.Empty));
        Assert.Equal(0xAF63DC4C8601EC8CUL, PlayerStatsReader.NameHash("a"));
        Assert.NotNull(new ItemNames().Find(0x00000010FE92A980));
    }
}
