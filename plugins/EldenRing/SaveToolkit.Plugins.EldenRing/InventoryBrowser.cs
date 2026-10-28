using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing;

internal sealed class InventoryBrowser(IReadOnlyList<SemanticCharacter> characters, ReferenceBrowser references,
    Func<string, string?> assetAttribution, Func<InventoryEntry, uint> quantity)
{
    public IReadOnlyList<QueryableCollection> Collections => characters.SelectMany(character => character.Containers.Select(container =>
        new QueryableCollection(Id(character.Slot, container.Id), $"Slot {character.Slot} · {container.Id}", Columns, container.Entries.Count)
        {
            Description = $"Inferred serialized rows, including internal placeholders. Capacity {container.Capacity}; "
                + $"declared common/key counts {container.CommonCount}/{container.KeyCount}. "
                + "Experimental positive ordinary-stack quantity edits; identity and structure remain protected."
                + (container.Warnings.Count == 0 ? "" : " " + string.Join(" ", container.Warnings)),
        })).ToArray();

    private static IReadOnlyList<QueryableColumn> Columns =>
    [
        new("item", "Item", new ReferenceSpec(ReferenceBrowser.CatalogId) { ReadOnly = true }),
        new("category", "Category"), new("quantity", "Quantity", IntegerSpec.UInt32),
        new("group", "Array"), new("handle", "Instance / direct handle"),
        new("index", "Inventory index", IntegerSpec.UInt32 with { ReadOnly = true }), new("resolution", "Resolution"),
        new("details", "Evidence / provenance"), new("refill", "Refill selected stacks"),
    ];

    public static string Id(int slot, string container) => $"slots/{slot}/inventory/{container}";

    public QueryableCollectionPage Query(string id, QueryableCollectionQuery query, Func<InventoryEntry, int, ViewNode> itemField,
        Func<InventoryEntry, int, ViewNode> quantityField)
    {
        foreach (var character in characters)
        foreach (var container in character.Containers)
        {
            if (id != Id(character.Slot, container.Id)) continue;
            var result = BrowseQuery.Apply(container.Entries, query, Columns, row => row.Key(character.Slot), Text, Number);
            var rows = result.Page.Select(entry => new TableRow(entry.Key(character.Slot), new Dictionary<string, ViewNode>
            {
                ["item"] = itemField(entry, character.Slot),
                ["category"] = new TextNode(entry.Category),
                ["quantity"] = quantityField(entry, character.Slot),
                ["group"] = new TextNode(entry.Group),
                ["handle"] = new TextNode($"0x{entry.Handle:X8}") { Monospace = true },
                ["index"] = new TextNode(entry.InventoryIndex.ToString(CultureInfo.InvariantCulture)),
                ["resolution"] = new TextNode(entry.Resolution),
                ["details"] = Detail(entry),
                ["refill"] = WritePlanning.RefillAction(character.Slot),
            })).ToArray();
            return new QueryableCollectionPage(id, Columns, rows, container.Entries.Count, result.Filtered, result.Offset, result.Limit);
        }
        throw new KeyNotFoundException($"Unknown collection '{id}'.");
    }

    private string Text(InventoryEntry entry, string column) => column switch
    {
        "item" => $"{entry.RawId} {references.Find(entry.RawId)?.Name}",
        "category" => entry.Category,
        "quantity" => quantity(entry).ToString(CultureInfo.InvariantCulture),
        "group" => entry.Group,
        "handle" => $"0x{entry.Handle:X8}",
        "index" => entry.InventoryIndex.ToString(CultureInfo.InvariantCulture),
        "resolution" => entry.Resolution,
        "details" => references.Find(entry.RawId) is { } record ? ReferenceBrowser.Details(record) : entry.RawId,
        "refill" => "Refill supported ordinary stacks",
        _ => throw new ArgumentException("Unknown inventory column."),
    };

    private long? Number(InventoryEntry entry, string column) => column switch
    {
        "quantity" => quantity(entry), "index" => entry.InventoryIndex, "handle" => entry.Handle, _ => null,
    };

    private SectionNode Detail(InventoryEntry entry)
    {
        var record = references.Find(entry.RawId);
        var nodes = new List<ViewNode>
        {
            new TextNode($"{entry.RawId}; file offset 0x{entry.Offset:X}; record {entry.Record}; {entry.Resolution}. "
                + "Inferred identity, no console capture. No legal quantity cap established."),
        };
        if (record is not null)
        {
            nodes.Add(new TextNode(ReferenceBrowser.Details(record)));
            if (record.AssetId is { } asset)
            {
                nodes.Add(new TextNode("Icon source: " + (assetAttribution(asset) ?? "No asset provenance available.")));
                nodes.Add(new ImageNode(asset, record.Name));
            }
        }
        return new SectionNode("Item details", nodes);
    }
}
