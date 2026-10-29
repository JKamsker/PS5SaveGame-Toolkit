using System.Buffers.Binary;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal sealed class FaithInventoryView(byte[] bytes, FaithInventory inventory, FaithCatalog catalog)
{
    public const string CollectionId = "main-inventory";
    public const int EagerLimit = 100;

    public QueryableCollection Collection => new(CollectionId, "Main allocated records (zero stacks included)", Columns, inventory.Items.Count)
    {
        Description = "Search/filter/sort covers every allocated record, including unloaded pages. " +
            "Select all matching rows to preview explicit quantities with exclusions. Allocation does not prove ownership.",
    };

    private static readonly QueryableColumn[] Columns = [
        new("icon", "Icon"),
        new("slot", "Storage slot", IntegerSpec.Int32), new("id", "Raw ID / reference", IntegerSpec.Int32),
        new("name", "Name"), new("category", "Category"), new("quantity", "Stored stack", IntegerSpec.Int32),
        new(FaithStoredStackState.ColumnKey, "Stored stack state"),
        new("flags", "Raw new-flag word"), new("details", "Evidence"), new("action", "Selected rows"),
    ];

    public IReadOnlyList<ViewNode> Build(Func<FaithItem, string, FieldNode> quantity, Func<string, int, FieldNode> identity) =>
    [
        new TextNode("Existing supported main/secondary records can be patched in place. No records are added or removed. " +
            FaithWriteMap.Warning) { Tone = Tone.Warning },
        new TextNode("Legacy manual edits use storage bounds only; reference names and limits do not establish current gameplay caps. " +
            "F6 explicit-quantity previews use an observed corpus ceiling; they do not calculate a legal max or refill capacity.") { Tone = Tone.Warning },
        new TextNode($"Named main records: {inventory.Items.Count(item => catalog.Item(item.Id) is not null)}/{inventory.Items.Count}. " +
            "Unmapped IDs remain visible. Categories describe the reference source, not ownership or availability."),
        new ActionNode(FaithCatalog.LookupActionId, "Inspect an item reference")
        {
            Mutates = false,
            Description = "Browse the attributed catalog by name or category and inspect a record. This action changes no save bytes or history.",
            Parameters = [new FieldNode(FaithCatalog.LookupParameter, "Catalog item",
                new ReferenceSpec(FaithCatalog.ItemsId), FieldValue.Of("1"))],
        },
        inventory.IsSupported ? inventory.Items.Count > EagerLimit
            ? new QueryableTableNode(Collection.Id, Collection.Title, Collection.Columns, Collection.TotalCount)
                { Description = Collection.Description }
            : Table(inventory.Items, "inventory", quantity, identity) :
            new TextNode("Main inventory schema/allocation is unsupported or ambiguous; use Structure/Hex.") { Tone = Tone.Warning },
        new SectionNode("Secondary bank — unconfirmed role, Experimental", [
            new TextNode("This is a separate stored bank. It may contain historical/stage data; it is not identified as current inventory. " +
                "ID zero is preserved without assigning a sentinel meaning.") { Tone = Tone.Warning },
            inventory.IsSecondarySupported ? Table(inventory.SecondaryItems, "secondary", quantity, identity) :
                new TextNode("Secondary allocation is not a supported dense bank; all bytes remain in Structure/Hex."),
        ]),
    ];

    public TableRow Row(FaithItem item, string bank, Func<FaithItem, string, FieldNode> quantity,
        Func<string, int, FieldNode> identity) => new($"{bank}/{item.Slot}", new Dictionary<string, ViewNode>
    {
        ["icon"] = catalog.Icon(catalog.Item(item.Id)),
        ["slot"] = new TextNode(FaithCatalog.Key(item.Slot)),
        ["id"] = identity($"{bank}/{item.Slot}/item", item.Id),
        ["name"] = new TextNode(catalog.Name(item.Id)),
        ["category"] = new TextNode(catalog.Category(item.Id)),
        ["quantity"] = quantity(item, bank),
        [FaithStoredStackState.ColumnKey] = new TextNode(FaithStoredStackState.Label(ReadInt(item.Offset + sizeof(int)))),
        ["flags"] = new TextNode($"0x{ReadInt(item.Offset + 2 * sizeof(int)):X8}"),
        ["details"] = new SectionNode("Source and storage", [new TextNode(catalog.Provenance(item.Id)),
            new TextNode($"Record at 0x{item.Offset:X}; allocation does not establish current ownership. " + FaithWriteMap.Warning)])
                { Collapsed = true },
        ["action"] = bank == "inventory"
            ? new SectionNode("Selected main-bank rows", [FaithQuantityPlanning.Action(), FaithCoveragePlanning.RestoreAction()])
            : new TextNode("No secondary bulk action"),
    });

    private TableNode Table(IReadOnlyList<FaithItem> items, string bank,
        Func<FaithItem, string, FieldNode> quantity, Func<string, int, FieldNode> identity) => new(
        Columns.Select(column => new TableColumn(column.Key, column.Label)).ToArray(),
        items.Select(item => Row(item, bank, quantity, identity)).ToArray())
    {
        Title = bank == "inventory" ? "Main allocated records (zero stacks included)" : "Secondary allocated records (unconfirmed role)",
        Description = bank == "inventory" ? "Select main rows, then use a table action to preview their explicit target quantities. " +
            "No selected rows means no changes. The separate action below the table has document scope." : "Stored stage/bank role remains unconfirmed.",
    };

    private int ReadInt(int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
}
