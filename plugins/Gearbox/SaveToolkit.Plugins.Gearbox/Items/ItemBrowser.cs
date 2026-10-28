using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

internal sealed class ItemBrowser
{
    private const string InspectAction = "catalogue/inspect";
    private const string InspectPartAction = "catalogue/inspect-part";
    private readonly GearboxLazyViews _lazy;
    private readonly GearboxDocument _document;
    private readonly ItemReferences _references;
    private readonly List<BrowserItem> _items = [];
    private readonly Dictionary<string, string> _lists;
    private static readonly QueryableColumn[] Columns = [new("index", "List index", IntegerSpec.Int32), new("name", "Family label"),
        new("category", "Part family (raw)"), new("level", "Level", IntegerSpec.Int32), new("status", "Decode status"), new("detail", "Details")];
    private static readonly QueryableColumn[] CatalogColumns = [new("name", "Family label"), new("category", "Part family (raw)"), new("asset", "Balance asset")];

    public ItemBrowser(GearboxDocument document, ProtoMessage root, bool profile, GearboxLazyViews lazy, IReferenceContext references)
    {
        _document = document;
        _lazy = lazy;
        _references = new(root.Schema.Wonderlands, references);
        _lists = profile ? new() { ["bank_inventory_list"] = "Bank", ["lost_loot_inventory_list"] = "Lost loot" }
            : new() { ["inventory_items"] = "Backpack" };
        var reader = new SerialReader(root.Schema.Wonderlands);
        var identity = 0;
        foreach (var list in _lists.Keys)
        {
            var index = 0;
            foreach (var field in root.All(list))
            {
                var serial = field.Message?.Last("item_serial_number")?.Value as byte[] ?? field.Value as byte[] ?? [];
                // IDs are allocated once per document, including duplicates. No structural writes are enabled.
                var item = new BrowserItem($"item-{identity++}", list, index++, serial,
                    field.Message?.Integer("flags"), field.Message?.Integer("pickup_order_index"), new(() => reader.Read(serial)));
                _items.Add(item);
            }
        }
    }

    public IReadOnlyList<QueryableCollection> Collections => [.. _lists.Select(l => new QueryableCollection(l.Key, l.Value, Columns,
        _items.Count(i => i.List == l.Key))), new("catalogue", "Balance catalogue", CatalogColumns, _references.Database.Records(ItemCatalogs.Balances).Count())];

    public IReadOnlyList<ViewNode> Build()
    {
        List<ViewNode> result = [new TextNode("Read-only serial browser. Family labels are not full generated item names. CRC/layout validation does not prove legal gear. Unknown serials and tails are preserved.") { Tone = Tone.Info }];
        result.Add(new TextNode(_references.Database.Records(ItemCatalogs.Widths).First().AttributionText) { Tone = Tone.Muted });
        result.AddRange(Collections.Select(c => new QueryableTableNode(c.Id, c.Title, c.Columns, c.TotalCount)
        { Description = "Search/filter/sort covers the complete collection. " + _references.Database.Pack.Title }));
        result.Add(new ActionNode(InspectAction, "Inspect catalogue entry")
        {
            Description = "Search the attributed balance catalogue; inspection does not change save bytes. No replacement or item creation.",
            Parameters = [new FieldNode("balance", "Balance / family", new ReferenceSpec(ItemCatalogs.Balances)
            {
                Assess = record => record.Attributes.GetValueOrDefault("databaseVersion") == _references.Database.MaximumVersion.ToString(CultureInfo.InvariantCulture)
                    ? new ReferenceEligibility() : new ReferenceEligibility { Eligible = false, Reasons = ["This label pack does not match the pinned serial database version."] },
            }, FieldValue.Of(string.Empty))],
        });
        result.Add(new ActionNode(InspectPartAction, "Inspect serial index / part")
        {
            Description = "Browse raw serial asset indices by category with attribution. No compatibility or legal-combination claim; no save changes.",
            Parameters = [new FieldNode("part", "Serial asset / part", new ReferenceSpec(ItemCatalogs.Indices), FieldValue.Of(string.Empty))],
        });
        return result;
    }

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        var collection = Collections.SingleOrDefault(c => c.Id == collectionId) ?? throw new KeyNotFoundException("Unknown Gearbox collection.");
        var columns = collection.Columns;
        if (query.Cursor is not null) throw new ArgumentException("Gearbox collections use offset paging.");
        if (query.SortColumn is { } sort && columns.All(c => c.Key != sort)) throw new ArgumentException("Unknown sort column.");
        if (query.Filters.Keys.Any(key => columns.All(c => c.Key != key))) throw new ArgumentException("Unknown filter column.");
        var entries = collectionId == "catalogue"
            ? _references.Database.Records(ItemCatalogs.Balances).Select(r => new BrowserEntry(r.Key,
                new Dictionary<string, string> { ["name"] = ItemReferences.Label(_references.Balance(r.Key), r.Key), ["category"] = r.Category ?? "Unmapped", ["asset"] = r.Key }, null))
            : _items.Where(i => i.List == collectionId).Select(Entry);
        entries = entries.Where(e => (string.IsNullOrEmpty(query.Text) || e.Values.Values.Any(v => v.Contains(query.Text, StringComparison.OrdinalIgnoreCase))
                || e.Item?.Info.Value.Balance?.Path?.Contains(query.Text, StringComparison.OrdinalIgnoreCase) == true)
            && query.Filters.All(f => e.Values.GetValueOrDefault(f.Key)?.Equals(f.Value, StringComparison.OrdinalIgnoreCase) == true));
        var filtered = entries.ToArray();
        var key = query.SortColumn ?? (collectionId == "catalogue" ? "asset" : "index");
        // Typed numeric ordering; stable identity breaks ties in either direction.
        var ordered = Sort(filtered, key, query.Descending);
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        var rows = ordered.ThenBy(e => e.Key, StringComparer.Ordinal).Skip(offset).Take(limit).Select(Row).ToArray();
        return new(collectionId, columns, rows, collection.TotalCount, filtered.Length, offset, limit);
    }

    private static int Number(string? text) => int.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value : -1;

    private static IOrderedEnumerable<BrowserEntry> Sort(IEnumerable<BrowserEntry> entries, string key, bool descending)
    {
        if (key is "index" or "level")
            return descending ? entries.OrderByDescending(e => Number(e.Values.GetValueOrDefault(key)))
                : entries.OrderBy(e => Number(e.Values.GetValueOrDefault(key)));
        return descending ? entries.OrderByDescending(e => e.Values.GetValueOrDefault(key), StringComparer.OrdinalIgnoreCase)
            : entries.OrderBy(e => e.Values.GetValueOrDefault(key), StringComparer.OrdinalIgnoreCase);
    }

    private BrowserEntry Entry(BrowserItem item)
    {
        var info = item.Info.Value;
        var record = _references.Balance(info.Balance?.Path);
        return new(item.Id, new Dictionary<string, string>
        {
            ["index"] = item.Index.ToString(CultureInfo.InvariantCulture), ["name"] = ItemReferences.Label(record, info.Balance?.Path),
            ["category"] = record?.Category ?? "Unmapped", ["level"] = info.Level?.ToString(CultureInfo.InvariantCulture) ?? "—",
            ["status"] = info.Complete ? "CRC / header / parts" : info.ChecksumValid ? "CRC / partial layout" : "Unknown / invalid",
        }, item);
    }

    private TableRow Row(BrowserEntry entry)
    {
        var cells = entry.Values.ToDictionary(p => p.Key, p => (ViewNode)new TextNode(p.Value));
        var record = _references.Balance(entry.Item?.Info.Value.Balance?.Path ?? entry.Key);
        cells["name"] = ReadOnly(entry.Key + "/name", "Family label", entry.Values["name"],
            record?.Attributes.GetValueOrDefault("displayKind") == "family" ? Confidence.Inferred : Confidence.Unknown, record?.AttributionText);
        cells["category"] = ReadOnly(entry.Key + "/category", "Raw part family", entry.Values["category"], Confidence.Unknown,
            _references.Database.Find(ItemCatalogs.Families, record?.Key ?? entry.Key)?.AttributionText ?? record?.AttributionText);
        if (entry.Item is { } item)
            cells["detail"] = _lazy.Register("detail/" + item.Id, "Serial and parts", () => Details(item));
        return new(entry.Key, cells);
    }

    private IReadOnlyList<ViewNode> Details(BrowserItem item)
    {
        var info = item.Info.Value;
        var record = _references.Balance(info.Balance?.Path);
        List<ViewNode> nodes = [new TextNode(info.Diagnostic) { Tone = info.Complete ? Tone.Info : Tone.Warning },
            ReadOnly(item.Id + "/balance", "Balance asset", info.Balance?.Path ?? $"index {info.Balance?.Index}", Confidence.Unknown, record?.AttributionText),
            new TextNode($"Serial v{info.Version}; database {info.DatabaseVersion}; flags {item.Flags}; pickup order {item.Pickup}."),
            AssetField(item.Id + "/inventory", "Inventory asset", info.Inventory),
            AssetField(item.Id + "/manufacturer", "Manufacturer asset", info.Manufacturer),
            new TextNode($"Rerolls: {info.Rerolls?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; Chaos raw tier: {info.Chaos?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}; remaining padding bits: {(info.Complete ? info.RemainingBits : "unknown")}."),
            new TextNode("Original serial bytes (BL3 wrapper is shared by both games; it does not identify the game):"),
            new TextNode(item.Serial.Length <= SerialEnvelope.MaximumLength ? $"BL3({Convert.ToBase64String(item.Serial)})" : "Oversized serial preserved; code display suppressed.") { Monospace = true }];
        var parts = info.Parts.Concat(info.GenericParts).Select((p, i) => new TableRow(i.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, ViewNode> { ["category"] = new TextNode(p.Category), ["index"] = new TextNode(p.Index.ToString(CultureInfo.InvariantCulture)),
                ["asset"] = ReadOnly(item.Id + "/part/" + i, "Raw part asset", p.Path ?? "Unmapped", p.Path is null ? Confidence.Unknown : Confidence.Verified,
                    _references.Database.Find(ItemCatalogs.Indices, $"{p.Category}:{p.Index}")?.AttributionText) })).ToArray();
        nodes.Add(new TableNode([new("category", "Category"), new("index", "Index"), new("asset", "Raw asset")], parts));
        return nodes;
    }

    private FieldNode ReadOnly(string id, string label, string value, Confidence confidence, string? source) =>
        _document.InspectionField(id, label, value, confidence, source);

    private FieldNode AssetField(string id, string label, SerialAsset? asset) =>
        ReadOnly(id, label, asset?.Path ?? $"Unmapped index {asset?.Index}", asset?.Path is null ? Confidence.Unknown : Confidence.Verified,
            asset is null ? null : _references.Database.Find(ItemCatalogs.Indices, $"{asset.Category}:{asset.Index}")?.AttributionText);

    public ActionOutcome? Inspect(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (actionId == InspectPartAction)
        {
            if (!parameters.TryGetValue("part", out var part) || part.Kind != FieldValueKind.Text || _references.Index(part.AsString()) is not { } index)
                throw new SaveFormatException("Choose a known serial asset from the catalogue.");
            return new ActionOutcome { Message = $"{index.Name}\n{index.Key}\n{index.Category}\nRaw serial index; legal combinations unknown.\n{index.AttributionText}" };
        }
        if (actionId != InspectAction) return null;
        if (!parameters.TryGetValue("balance", out var value) || value.Kind != FieldValueKind.Text || _references.Balance(value.AsString()) is not { } record)
            throw new SaveFormatException("Choose a known balance from the catalogue.");
        return new ActionOutcome { Message = $"{ItemReferences.Label(record, record.Key)}\n{record.Key}\n{record.Description}\n{record.AttributionText}" };
    }

    private sealed record BrowserItem(string Id, string List, int Index, byte[] Serial, long? Flags, long? Pickup, Lazy<SerialInfo> Info);
    private sealed record BrowserEntry(string Key, Dictionary<string, string> Values, BrowserItem? Item);
}
