using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

internal static class PerformanceInventory
{
    internal static InventoryState Create(int count, IReferenceCatalog? catalog = null)
    {
        var elements = Enumerable.Range(0, count).Select(i =>
            new TaggedProperty(i.ToString(), "StructElement", "", 0, default, default, null) { Children = [
                Scalar("ItemDataAsset", "ObjectProperty", FieldValue.Of($"/Game/DA_Item_Synthetic{i}.DA_Item_Synthetic{i}"), i * 3),
                Scalar("Amount", "IntProperty", FieldValue.Of(i + 1), i * 3 + 1),
                Scalar("ItemId", "Int64Property", FieldValue.Of(i), i * 3 + 2),
            ] }).ToArray();
        var slots = new TaggedProperty("Slots", "ArrayProperty", "StructProperty", 0, default, default, null) { Children = elements };
        var list = new TaggedProperty("ResourcesList", "StructProperty", "InventoryList", 0, default, default, null) { Children = [slots] };
        var record = new ObjectRecord("synthetic", RecordKinds.Component, "player", "Inventory", InventoryClasses.Player, "", default, default, [], [list], default, []);
        return new(new([], [], [record], []), catalog);
    }

    internal static InventoryCollection Collection(InventoryState state) => new(state,
        new InventoryViews(state, (id, label, spec, get, _, _) => new(id, label, spec, get())));

    private static TaggedProperty Scalar(string name, string type, FieldValue value, int offset) =>
        new(name, type, "", 0, new(offset, 4), new(offset, 4), value);
}
