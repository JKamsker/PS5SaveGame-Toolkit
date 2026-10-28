using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Editing;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

internal sealed class InventoryView(PragmataDocument document, LazyPages pages)
{
    public IReadOnlyList<ViewNode> Build() => [
        new TextNode("Player-facing item mappings and legal stack limits are unverified. Attributed reference packs can supply names, descriptions and images; raw IDs are always preserved. Quantities use the stored int32 range.") { Tone = Tone.Warning },
        document.Layout.Items.Count <= LazyPages.PageSize ? Table(0, document.Layout.Items.Count) :
            pages.Section("inventory", "_InventoryItems", document.Layout.Items.Count, (start, count) => [Table(start, count)]),
        QuantityAction(),
    ];

    private static ActionNode QuantityAction() => new(PragmataViews.QuantityAction, "Set stored quantities to N")
        {
            SupportsPlanning = true,
            Tone = Tone.Warning,
            Parameters = [new FieldNode("quantity", "N", IntegerSpec.Int32 with { Min = 0 }, FieldValue.Of(1L))],
            Description = "Preview selected rows from the table, or every existing row from document scope. Legal limits and game effects remain unverified. " +
                "Expand target quantity pages first; applying is one undo step. Does not add items or set unlock flags.",
        };

    private TableNode Table(int start, int count)
    {
        var equipped = document.Read(document.Layout.Inventory!.Find(KnownHashes.EquippedItem)!).AsUInt32();
        var rows = document.Layout.Items.Skip(start).Take(count).Select(item =>
        {
            var id = document.Read(item.Find(KnownHashes.ItemId)!).AsUInt32();
            return new TableRow(QuantityActions.RowKey(item), new Dictionary<string, ViewNode>
            {
                ["id"] = new TextNode($"0x{id:x8} / {id}") { Monospace = true },
                ["name"] = document.References.ItemField(item.Find(KnownHashes.ItemId)!),
                ["quantity"] = document.Field(item.Find(KnownHashes.Quantity)!) with { Label = "Quantity" },
                ["equipped"] = new TextNode(id == equipped ? "Equipped" : ""),
                ["details"] = new SectionNode("Item details and sources", document.References.ItemDetails(id)),
                ["action"] = QuantityAction(),
            });
        }).ToArray();
        return new TableNode([new("name", "Item (read-only reference)"), new("id", "Raw ID (hex / decimal)"),
            new("quantity", "Quantity"), new("equipped", "Equipped"), new("details", "Details / sources"), new("action", "Selection action")], rows)
        {
            Title = "_InventoryItems",
        };
    }
}
