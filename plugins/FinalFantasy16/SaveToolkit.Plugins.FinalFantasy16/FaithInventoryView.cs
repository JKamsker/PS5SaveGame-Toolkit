using System.Buffers.Binary;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal sealed class FaithInventoryView(byte[] bytes, FaithInventory inventory, FaithCatalog catalog)
{
    public IReadOnlyList<ViewNode> Build(Func<FaithItem, FieldNode> quantity, Func<string, int, FieldNode> identity) =>
    [
        new TextNode("Only existing main-bank quantities can be edited. Identity pickers are read-only: " +
            "replacing, adding, removing or equipping items requires controlled evidence.") { Tone = Tone.Warning },
        new TextNode("Legacy manual edits use storage bounds only; reference names and limits do not establish current gameplay caps. " +
            "F6 refill/set-quantity bulk actions are unavailable until active-bank, capacity and dependency evidence is supplied.") { Tone = Tone.Warning },
        new TextNode($"Named main records: {inventory.Items.Count(item => catalog.Item(item.Id) is not null)}/{inventory.Items.Count}. " +
            "Unmapped IDs remain visible. Categories describe the reference source, not ownership or availability."),
        new ActionNode(FaithCatalog.LookupActionId, "Look up an item")
        {
            Description = "Search the attributed catalog by name or category and inspect a record. This lookup changes no save bytes.",
            Parameters = [new FieldNode(FaithCatalog.LookupParameter, "Catalog item",
                new ReferenceSpec(FaithCatalog.ItemsId), FieldValue.Of("1"))],
        },
        inventory.IsSupported ? Table(inventory.Items, "inventory", quantity, identity) :
            new TextNode("Main inventory schema/allocation is unsupported or ambiguous; use Structure/Hex.") { Tone = Tone.Warning },
        new SectionNode("Secondary bank — unconfirmed role, read-only", [
            new TextNode("This is a separate stored bank. It may contain historical/stage data; it is not identified as current inventory. " +
                "ID zero is preserved without assigning a sentinel meaning.") { Tone = Tone.Warning },
            inventory.IsSecondarySupported ? Table(inventory.SecondaryItems, "secondary", null, identity) :
                new TextNode("Secondary allocation is not a supported dense bank; all bytes remain in Structure/Hex."),
        ]),
    ];

    private TableNode Table(IReadOnlyList<FaithItem> items, string bank,
        Func<FaithItem, FieldNode>? quantity, Func<string, int, FieldNode> identity) => new([
        new TableColumn("slot", "Storage slot"), new TableColumn("id", "Raw ID / reference"),
        new TableColumn("name", "Name"), new TableColumn("category", "Category"),
        new TableColumn("quantity", "Stored stack"), new TableColumn("flags", "Raw new-flag word"),
    ], items.Select(item => new TableRow($"{bank}/{item.Slot}", new Dictionary<string, ViewNode>
    {
        ["slot"] = new TextNode(FaithCatalog.Key(item.Slot)),
        ["id"] = identity($"{bank}/{item.Slot}/item", item.Id),
        ["name"] = new TextNode(catalog.Name(item.Id)),
        ["category"] = new TextNode(catalog.Category(item.Id)),
        ["quantity"] = quantity is null
            ? new TextNode(FaithCatalog.Key(ReadInt(item.Offset + sizeof(int)))) : quantity(item),
        ["flags"] = new TextNode($"0x{ReadInt(item.Offset + 2 * sizeof(int)):X8}"),
    })).ToArray())
    {
        Title = bank == "inventory" ? "Main allocated records (zero stacks included)" : "Secondary allocated records (unconfirmed role)",
    };

    private int ReadInt(int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
}
