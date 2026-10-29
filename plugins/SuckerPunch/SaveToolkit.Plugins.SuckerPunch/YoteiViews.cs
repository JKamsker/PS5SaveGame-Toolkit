using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch;

internal sealed class YoteiViews(byte[] bytes, YoteiState state, IReferenceContext references, Func<FieldNode, FieldNode> bind)
{
    private readonly CollectionQueryCache<YoteiRecord> _queries = new();
    public const string ItemsCatalog = "suckerpunch_yotei_items";
    public const string GearCollectionId = "yotei/gear-registry";
    public const string InventorySelectionId = "yotei/inventory";
    public const string TechniquesCatalog = "suckerpunch_yotei_techniques";
    public const string WorldCatalog = "suckerpunch_yotei_world";
    public static IReadOnlyList<string> SourceUrls =>
    [
        "https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/22",
        "https://www.powerpyx.com/ghost-of-yotei-all-hot-spring-locations/",
        "https://www.powerpyx.com/ghost-of-yotei-all-collectibles-locations-guide/",
        "https://www.youtube.com/watch?v=_H2a_RaapdM",
        "https://ghostfranchise.fandom.com/wiki/Young_Bamboo",
        "https://digital2buy.com/es/producto/ghost-of-yotei-espanol-guardado-modificado-goyotei-mod-goy-ps5-mods-yotei-ghost-of-yotei",
    ];
    public const string HotSpringUrl = "https://www.powerpyx.com/ghost-of-yotei-all-hot-spring-locations/";
    private static IReadOnlyList<QueryableColumn> GearColumns =>
    [new("id", "Raw registry identifier"), new("category", "Name family"), new("a", "Byte A", IntegerSpec.UInt8),
        new("b", "Byte B", IntegerSpec.UInt8), new("state", "Meaning")];
    public QueryableCollection GearCollection => new(GearCollectionId, "Gear registry (not ownership)", GearColumns, state.Gear.Count);
    public QueryableTableNode GearTable => new(GearCollectionId, GearCollection.Title, GearColumns, state.Gear.Count);

    public TableNode Inventory(Func<YoteiRecord, FieldNode> quantity, Func<YoteiRecord, FieldNode> copy)
    {
        var catalog = references.Catalog(ItemsCatalog);
        return new TableNode([new("icon", "Image"), new("id", "Raw ID"), new("name", "Reference"),
            new("quantity", "Quantity"), new("copy", "Ammo count copy"), new("evidence", "Evidence")],
            state.Inventory.Select(row =>
            {
                var reference = catalog?.Find(row.Name);
                return new TableRow(row.Name, new Dictionary<string, ViewNode>
                {
                    ["icon"] = Icon(reference), ["id"] = new TextNode(row.Name),
                    ["name"] = Reference(ItemsCatalog, row.Name, reference, "yotei/reference/items/" + row.Name),
                    ["quantity"] = quantity(row), ["copy"] = row.Ammo ? copy(row) : new TextNode("—"),
                    ["evidence"] = new TextNode(reference?.Description ?? "Unknown game-name mapping; raw identifier retained.") { Tone = Tone.Warning },
                });
            }).ToArray()) { SelectionId = InventorySelectionId, Title = "Existing Yōtei inventory" };
    }

    public TableNode Statistics() => new([new("id", "Raw statistic identifier"), new("value", "Stored uint32"), new("meaning", "Evidence")],
        state.Statistics.Select(row => new TableRow(row.Name, new Dictionary<string, ViewNode>
        {
            ["id"] = new TextNode(row.Name),
            ["value"] = bind(new FieldNode("yotei/statistics/" + row.Name, row.Name, IntegerSpec.UInt32 with { ReadOnly = true },
                FieldValue.Of((ulong)YoteiFormat.Word(bytes, row.DataOffset)))
            { Confidence = Confidence.Inferred, Description = "Counted 33-name statistics table; not a live ownership, quest or reward flag. In-game meaning unverified." }),
            ["meaning"] = new TextNode("Inferred statistic; discovery, completion and rewards remain Unknown."),
        })).ToArray());

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (collectionId != GearCollectionId) throw new KeyNotFoundException($"Unknown collection '{collectionId}'.");
        if (query.Limit is < 1 or > QueryableCollectionLimits.MaxPageSize || query.Offset < 0 || query.Cursor is not null)
            throw new ArgumentException("Use a non-negative offset, a page size of 1–200 and no cursor.", nameof(query));
        if (query.SortColumn is not (null or "id" or "category" or "a" or "b" or "state") ||
            query.Filters.Keys.Any(key => key is not ("category" or "state" or "id")))
            throw new ArgumentException("Unsupported gear filter or sort column.", nameof(query));
        var matches = _queries.Get(query, () => SelectGear(query));
        return new(collectionId, GearColumns, matches.Skip(query.Offset).Take(query.Limit).Select(row => new TableRow(row.Name,
            new Dictionary<string, ViewNode>
            {
                ["id"] = new TextNode(row.Name), ["category"] = new TextNode(Family(row.Name)),
                ["a"] = RawByte(row, 0), ["b"] = RawByte(row, 1), ["state"] = new TextNode("Unknown") { Tone = Tone.Warning },
            })).ToArray(), state.Gear.Count, matches.Length, query.Offset, query.Limit);
    }

    private YoteiRecord[] SelectGear(QueryableCollectionQuery query)
    {
        IEnumerable<YoteiRecord> rows = state.Gear;
        if (!string.IsNullOrWhiteSpace(query.Text)) rows = rows.Where(row => row.Name.Contains(query.Text, StringComparison.OrdinalIgnoreCase));
        foreach (var filter in query.Filters)
            rows = rows.Where(row => (filter.Key switch { "category" => Family(row.Name), "state" => "Unknown", _ => row.Name })
                .Equals(filter.Value, StringComparison.OrdinalIgnoreCase));
        var ordered = query.SortColumn switch
        {
            "a" => Sort(rows, row => bytes[row.DataOffset], query.Descending),
            "b" => Sort(rows, row => bytes[row.DataOffset + 1], query.Descending),
            "category" => Sort(rows, row => Family(row.Name), query.Descending, StringComparer.OrdinalIgnoreCase),
            "state" => rows.OrderBy(row => row.Name, StringComparer.Ordinal),
            _ => Sort(rows, row => row.Name, query.Descending, StringComparer.OrdinalIgnoreCase),
        };
        return ordered.ToArray();
    }

    private FieldNode RawByte(YoteiRecord row, int index) => bind(new FieldNode($"yotei/gear/{row.Name}/byte{index}",
        $"{row.Name} byte {index}", IntegerSpec.UInt8 with { ReadOnly = true }, FieldValue.Of((ulong)bytes[row.DataOffset + index]))
    { Confidence = Confidence.Unknown, Description = "Opaque registry byte; identical across all generations. Does not prove ownership or equipment state." });

    private static IOrderedEnumerable<YoteiRecord> Sort<T>(IEnumerable<YoteiRecord> rows, Func<YoteiRecord, T> key,
        bool descending, IComparer<T>? comparer = null) => (descending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer))
        .ThenBy(row => row.Name, StringComparer.Ordinal);

    private static string Family(string name) => name.StartsWith("Half", StringComparison.Ordinal) || name.StartsWith("Full", StringComparison.Ordinal)
        ? "Mask" : name.StartsWith("Body", StringComparison.Ordinal) ? "Body" : name.StartsWith("Charm", StringComparison.Ordinal)
            ? "Charm" : name.StartsWith("Helm", StringComparison.Ordinal) || name.StartsWith("Hats", StringComparison.Ordinal) ||
                name.StartsWith("Band", StringComparison.Ordinal) ? "Headwear" : "Other";

    private FieldNode Reference(string catalog, string key, ReferenceRecord? record, string valueId) => bind(new FieldNode(valueId,
        record?.Name ?? key, new ReferenceSpec(catalog) { ReadOnly = true, AllowCustom = true }, FieldValue.Of(key))
    { Confidence = record?.Attributes.GetValueOrDefault("mappingConfidence") == "Inferred" ? Confidence.Inferred : Confidence.Unknown,
        Description = (record?.Description ?? "No pinned reference record; raw identity retained.") + " " + record?.AttributionText });

    private ViewNode Icon(ReferenceRecord? record) => record?.AssetId is { } id && references.GetAsset(id) is not null
        ? new ImageNode(id, record.Name) : new TextNode("—");

    public IReadOnlyList<ViewNode> Techniques() => new YoteiSpatialViews(references, Reference).Techniques();
    public IReadOnlyList<ViewNode> World() => new YoteiSpatialViews(references, Reference).World();
}
