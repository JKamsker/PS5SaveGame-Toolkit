using System.Globalization;
using System.Text.RegularExpressions;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal sealed class FaithCatalog(IReferenceContext references)
{
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
    public ReferenceRecord? Find(string catalog, int id) => references.Catalog(catalog)?.Find(Key(id)) ?? FaithEmbeddedCatalogs.Find(catalog, Key(id));
    public IReadOnlyList<ReferenceRecord> Search(string catalog, ReferenceQuery query) =>
        references.Catalog(catalog)?.Search(query).Items ?? FaithEmbeddedCatalogs.Search(catalog, query).Items;
    public ReferenceRecord? Item(int id) => Find(ItemsId, id) ?? Find(WikiItemsId, id);
    public string Name(int id) => Item(id) is { } record ? PlainText(record.Name) : $"Unknown item {id}";
    public string Category(int id) => Item(id)?.Category ?? "unmapped";
    public ViewNode Icon(ReferenceRecord? record) => record?.AssetId is { } asset && references.AssetProvenance(asset) is not null
        ? new ImageNode(asset, PlainText(record.Name)) : new TextNode("Icon unavailable");
    public string Provenance(int id)
    {
        if (Item(id) is not { } record) return "No reference name mapping is available; raw ID is preserved.";
        var original = Find(WikiItemsId, id);
        return Describe(record) + (original is not null && original.Dataset.PackId != record.Dataset.PackId
            ? " Earlier curated reference: " + Describe(original) : "");
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

}
