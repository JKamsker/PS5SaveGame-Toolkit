using System.Globalization;
using System.Text.RegularExpressions;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal sealed class FaithCatalog(IReferenceContext references)
{
    // Leave room for replacements, but never grow with an unlimited sequence of inspected IDs.
    private const int MaximumCachedItems = 2 * FaithInventory.Capacity;
    private readonly Dictionary<string, IReferenceCatalog?> _catalogs = new(StringComparer.Ordinal);
    private readonly Dictionary<int, ItemPresentation> _items = [];
    public const string ItemsId = "ffxvi_published_items";
    public const string WikiItemsId = "ffxvi_items";
    public const string PublishedMapsId = "ffxvi_published_maps";
    public const string BattleNpcsId = "ffxvi_published_bnpc";
    public const string EventNpcsId = "ffxvi_published_enpc";
    public const string MapsId = "ffxvi_maps";
    public const string SummonsId = "ffxvi_summons";
    public const string CommandsId = "ffxvi_commands";
    public const string GrowthId = "ffxvi_growth";
    public const string SkillsId = "ffxvi_skills";
    public const string FeatsId = "ffxvi_feats";
    public const string AbilitiesId = "ffxvi_abilities";
    public const string LookupActionId = "lookup-item";
    public const string LookupParameter = "item";
    public ReferenceRecord? Find(string catalog, int id) => Catalog(catalog)?.Find(Key(id)) ?? FaithEmbeddedCatalogs.Find(catalog, Key(id));
    public IReadOnlyList<ReferenceRecord> Search(string catalog, ReferenceQuery query) =>
        Catalog(catalog)?.Search(query).Items ?? FaithEmbeddedCatalogs.Search(catalog, query).Items;
    public ReferenceRecord? Item(int id) => Presentation(id).Record;
    public string Name(int id) => Presentation(id).Name;
    public string Category(int id) => Presentation(id).Category;
    public ViewNode Icon(ReferenceRecord? record) => record?.AssetId is { } asset && references.AssetProvenance(asset) is not null
        ? new ImageNode(asset, PlainText(record.Name)) : new TextNode("Icon unavailable");
    public string Provenance(int id)
    {
        var item = Presentation(id);
        if (item.Provenance is { } cached) return cached;
        if (item.Record is not { } record) return "No reference name mapping is available; raw ID is preserved.";
        var original = Find(WikiItemsId, id);
        item.Provenance = Describe(record) + (original is not null && original.Dataset.PackId != record.Dataset.PackId
            ? " Earlier curated reference: " + Describe(original) : "");
        return item.Provenance;
    }
    public static string Describe(ReferenceRecord record) => $"{record.AttributionText} Mapping confidence: Inferred. {record.Description}";
    public IReadOnlyList<ReferenceRecord> SkillReferences() => Search(SkillsId,
        new ReferenceQuery { Category = "skill-research", SortBy = ReferenceSortKey.Key, Limit = 68 });
    public IReadOnlyList<ReferenceRecord> Abilities() => Search(AbilitiesId,
        new ReferenceQuery { SortBy = ReferenceSortKey.Category, Limit = 100 });
    public static string Key(int id) => id.ToString(CultureInfo.InvariantCulture);

    // Remove only documented game presentation tags. Unknown markup remains literal text;
    // neither this plugin nor the view protocol treats catalog text as HTML.
    public static string PlainText(string value) => Regex.Replace(value,
        @"</?i>|<color=[a-zA-Z0-9_]+>|</color>", "", RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private IReferenceCatalog? Catalog(string id)
    {
        // Contexts are pinned and immutable. Resolving once also records dataset usage in Sources.
        if (_catalogs.TryGetValue(id, out var cached)) return cached;
        var catalog = references.Catalog(id);
        _catalogs.Add(id, catalog);
        return catalog;
    }

    private ItemPresentation Presentation(int id)
    {
        if (_items.TryGetValue(id, out var cached)) return cached;
        var item = new ItemPresentation(id, Find(ItemsId, id) ?? Find(WikiItemsId, id));
        if (_items.Count < MaximumCachedItems) _items.Add(id, item);
        return item;
    }

    private sealed class ItemPresentation(int id, ReferenceRecord? record)
    {
        public ReferenceRecord? Record { get; } = record;
        public string Name { get; } = record is null ? $"Unknown item {id}" : PlainText(record.Name);
        public string Category { get; } = record?.Category ?? "unmapped";
        public string? Provenance { get; set; }
    }
}
