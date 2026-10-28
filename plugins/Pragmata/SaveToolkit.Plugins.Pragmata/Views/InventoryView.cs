using System.Globalization;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

internal sealed class InventoryView(PragmataDocument document, LazyPages pages)
{
    public IReadOnlyList<ViewNode> Build() => [
        new TextNode("Item names and legal stack limits are unknown. IDs are preserved; quantities use the stored int32 range.") { Tone = Tone.Warning },
        document.Layout.Items.Count <= LazyPages.PageSize ? Table(0, document.Layout.Items.Count) :
            pages.Section("inventory", "_InventoryItems", document.Layout.Items.Count, (start, count) => [Table(start, count)]),
        new ActionNode(PragmataViews.QuantityAction, "Set quantity of all items to N")
        {
            Tone = Tone.Warning, Confirm = "Change every existing item quantity? Legal stack limits and console loading are untested." +
                (document.Layout.Items.Count > LazyPages.PageSize ? " Expand every quantity page first to retain undo; otherwise this action clears undo history." : ""),
            Parameters = [new FieldNode("quantity", "N", IntegerSpec.Int32 with { Min = 0 }, FieldValue.Of(1L))],
            Description = "Changes existing quantities atomically; includes zero-count items. Does not add items or set unlock flags.",
        },
    ];

    private TableNode Table(int start, int count)
    {
        var equipped = document.Read(document.Layout.Inventory!.Find(KnownHashes.EquippedItem)!).AsUInt32();
        var rows = document.Layout.Items.Skip(start).Take(count).Select((item, index) =>
        {
            var id = document.Read(item.Find(KnownHashes.ItemId)!).AsUInt32();
            return new TableRow((start + index).ToString(CultureInfo.InvariantCulture), new Dictionary<string, ViewNode>
            {
                ["id"] = new TextNode($"0x{id:x8} / {id}") { Monospace = true },
                ["quantity"] = document.Field(item.Find(KnownHashes.Quantity)!) with { Label = "Quantity" },
                ["equipped"] = new TextNode(id == equipped ? "Equipped" : ""),
            });
        }).ToArray();
        return new TableNode([new("id", "Item ID (hex / decimal)"), new("quantity", "Quantity"), new("equipped", "Equipped")], rows)
        {
            Title = "_InventoryItems",
        };
    }
}
