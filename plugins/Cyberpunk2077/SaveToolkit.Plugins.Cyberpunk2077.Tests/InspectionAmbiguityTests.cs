using System.Buffers.Binary;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class InspectionAmbiguityTests
{
    [Fact]
    public void EquipmentIndexRetainsDuplicateFullIdentitiesAsAmbiguousInsteadOfSelectingAnInstance()
    {
        var original = CsavContainer.Parse(ProgressionSampleTests.Files()[0].ReadAllBytes());
        var tree = CsavTree.Parse(original);
        var changed = original.Payload.ToArray();
        var source = tree.Nodes[55].Offset;
        var duplicate = tree.Nodes[56].Offset;
        original.Payload.AsSpan(source + 4, ItemIdentity.WireSize).CopyTo(changed.AsSpan(duplicate + 4));
        original.Payload.AsSpan(source + 4, ItemIdentity.WireSize).CopyTo(changed.AsSpan(duplicate - ItemIdentity.WireSize));
        var container = CsavContainer.Parse(original.Rebuild(changed));
        var items = InventoryReader.Read(container, tree);
        var equipment = EquipmentData.Read(new ScriptableReader(container, tree), container, tree, items);
        var shirt = Assert.Single(equipment.Slots, slot => slot.Area == "InnerChest");
        Assert.Null(shirt.Inventory);
        Assert.Equal("Ambiguous; multiple matching instances", shirt.Match);
        Assert.Equal("Not listed in decoded owner-1 loadout", equipment.State(55));
        Assert.Equal("Not listed in decoded owner-1 loadout", equipment.State(56));
    }

    [Fact]
    public void StatIndexRetainsHashCollisionsAsAmbiguousInsteadOfSelectingAModifierSet()
    {
        var original = CsavContainer.Parse(ProgressionSampleTests.Files()[0].ReadAllBytes());
        var tree = CsavTree.Parse(original);
        var items = InventoryReader.Read(original, tree);
        var node = Assert.Single(tree.Nodes, node => node.Name == "StatsSystem");
        var package = new RedPackage(ProgressionData.PackageBytes(original.Payload, node), false);
        var root = package.Chunk(0);
        var keys = root.StructArray(root.Require("keys", "array:gameStatsObjectID"));
        var itemKeys = keys.Where(key => key.Enum("idType", "gameStatIDType", "Invalid") == "ItemID").ToArray();
        var item = items.First(item => itemKeys.Any(key => ScriptableReader.UInt64(key, "entityHash", "Uint64") ==
            ItemStatsInspection.InstanceHash(ItemIdentity.FromInventory(item, original, tree))));
        var hash = ItemStatsInspection.InstanceHash(ItemIdentity.FromInventory(item, original, tree));
        var other = itemKeys.First(key => ScriptableReader.UInt64(key, "entityHash", "Uint64") != hash);
        var changed = original.Payload.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(changed.AsSpan(node.Offset + 8 + other.Require("entityHash", "Uint64").Offset), hash);
        var container = CsavContainer.Parse(original.Rebuild(changed));
        var inspection = new ItemStatsInspection(container, tree, items);
        Assert.Null(inspection.Warning);
        var rows = inspection.Table.Query(new QueryableCollectionQuery { Filters = new Dictionary<string, string>
            { ["node"] = item.NodeId.ToString(System.Globalization.CultureInfo.InvariantCulture), ["part"] = "inventory" } }).Rows;
        Assert.Contains(rows, row => ReadOnlyInspectionSampleTests.Cell(row, "details") == "Ambiguous stats key; not joined");
        Assert.DoesNotContain(rows, row => ReadOnlyInspectionSampleTests.Cell(row, "details").Contains("Unique saved ItemID", StringComparison.Ordinal));
    }
}
