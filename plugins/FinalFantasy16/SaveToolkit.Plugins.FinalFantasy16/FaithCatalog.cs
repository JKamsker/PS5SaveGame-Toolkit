using System.Globalization;
using System.Text.RegularExpressions;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal sealed class FaithCatalog(IReferenceContext references)
{
    public const string ItemsId = "ffxvi_items";
    public const string MapsId = "ffxvi_maps";
    public const string SummonsId = "ffxvi_summons";
    public const string CommandsId = "ffxvi_commands";
    public const string GrowthId = "ffxvi_growth";
    public const string SkillsId = "ffxvi_skills";
    public const string FeatsId = "ffxvi_feats";
    public const string LookupActionId = "lookup-item";
    public const string LookupParameter = "item";
    public ReferenceRecord? Find(string catalog, int id) => references.Catalog(catalog)?.Find(Key(id)) ?? FaithEmbeddedCatalogs.Find(catalog, Key(id));
    public ReferenceRecord? Item(int id) => Find(ItemsId, id);
    public string Name(int id) => Item(id) is { } record ? PlainText(record.Name) : $"Unknown item {id}";
    public string Category(int id) => Item(id)?.Category ?? "unmapped";
    public string Provenance(int id) => Item(id) is { } record ? Describe(record) : "No licensed name mapping is available; raw ID is preserved.";
    public static string Describe(ReferenceRecord record) => $"{record.AttributionText} Mapping confidence: Inferred. {record.Description}";
    public IReadOnlyList<ReferenceRecord> SkillReferences() => references.Catalog(SkillsId)?.Search(new ReferenceQuery { Limit = 68 }).Items
        ?? FaithEmbeddedCatalogs.Records(SkillsId, 68);
    public static string Key(int id) => id.ToString(CultureInfo.InvariantCulture);

    // Remove only documented game presentation tags. Unknown markup remains literal text;
    // neither this plugin nor the view protocol treats catalog text as HTML.
    public static string PlainText(string value) => Regex.Replace(value,
        @"</?i>|<color=[a-zA-Z0-9_]+>|</color>", "", RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

}
