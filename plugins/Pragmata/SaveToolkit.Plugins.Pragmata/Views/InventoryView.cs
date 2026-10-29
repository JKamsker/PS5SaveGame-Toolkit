using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Editing;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

internal sealed class InventoryView(PragmataDocument document)
{
    public IReadOnlyList<ViewNode> Build() => [
        new TextNode("Player-facing item mappings and legal stack limits are unverified. Attributed reference packs can supply names, descriptions and images; raw IDs are always preserved. Quantities use the stored int32 range.") { Tone = Tone.Warning },
        document.Layout.Items.Count <= LazyPages.PageSize ? Table() : document.InventoryRows.Table(),
        QuantityAction(),
    ];

    private static ActionNode QuantityAction() => new(PragmataViews.QuantityAction, "Set stored quantities")
        {
            SupportsPlanning = true,
            Tone = Tone.Warning,
            Parameters = [new FieldNode("quantity", "Target quantity", IntegerSpec.Int32 with { Min = 0 }, FieldValue.Of(1L))],
            Description = PragmataDocument.EditWarning + " Preview selected rows from the table, or every existing row from document scope. Legal limits and game effects remain unverified. " +
                "For large inventories, select all matching rows to include unloaded pages. Document scope requires loaded quantity bindings. Applying is one undo step. Does not add items or set unlock flags.",
        };

    private TableNode Table() => new(InventoryCollections.Columns.Select(column => new TableColumn(column.Key, column.Label)).ToArray(),
        document.Layout.Items.Select(item => Row(document, item)).ToArray())
        { Title = InventoryCollections.Title, Description = InventoryCollections.NativeArrayDescription };

    internal static TableRow Row(PragmataDocument document, SaveNode item)
    {
        var equipped = document.Read(document.Layout.Inventory!.Find(KnownHashes.EquippedItem)!).AsUInt32();
        var id = document.Read(item.Find(KnownHashes.ItemId)!).AsUInt32();
        return new TableRow(QuantityActions.RowKey(item), new Dictionary<string, ViewNode>
            {
                ["id"] = new TextNode($"0x{id:x8} / {id}") { Monospace = true },
                ["name"] = document.References.ItemField(item.Find(KnownHashes.ItemId)!),
                ["category"] = new TextNode(document.References.Item(id)?.Category ?? "unmapped"),
                ["quantity"] = document.Field(item.Find(KnownHashes.Quantity)!) with { Label = "Quantity" },
                ["equipped"] = new TextNode(id == equipped ? "Equipped" : ""),
                ["details"] = new SectionNode("Item details and sources", document.References.ItemDetails(id)),
                ["action"] = QuantityAction(),
        });
    }
}
