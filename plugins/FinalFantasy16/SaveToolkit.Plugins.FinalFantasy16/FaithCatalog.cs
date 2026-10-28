using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
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
    public const string LookupActionId = "lookup-item";
    public const string LookupParameter = "item";
    private const string EmbeddedPack = "refpacks/ffxvi.wiki.en/";
    private static readonly FrozenDictionary<int, ReferenceRecord> Fallback = LoadFallback();
    private readonly IReferenceCatalog? _items = references.Catalog(ItemsId);

    public ReferenceRecord? Item(int id) => _items?.Find(Key(id)) ?? Fallback.GetValueOrDefault(id);
    public string Name(int id) => Item(id) is { } record ? PlainText(record.Name) : $"Unknown item {id}";
    public string Category(int id) => Item(id)?.Category ?? "unmapped";
    public string Provenance(int id) => Item(id)?.AttributionText ?? "No licensed name mapping is available; raw ID is preserved.";
    public static string Key(int id) => id.ToString(CultureInfo.InvariantCulture);

    // Remove only documented game presentation tags. Unknown markup remains literal text;
    // neither this plugin nor the view protocol treats catalog text as HTML.
    public static string PlainText(string value) => Regex.Replace(value,
        @"</?i>|<color=[a-zA-Z0-9_]+>|</color>", "", RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static FrozenDictionary<int, ReferenceRecord> LoadFallback()
    {
        using var manifestStream = Resource("pack.json");
        using var manifest = JsonDocument.Parse(manifestStream);
        var attribution = manifest.RootElement.GetProperty("datasets")[0].GetProperty("attribution");
        var dataset = new ReferenceDatasetInfo("ffxvi.wiki.en", ItemsId, ReferenceDatasetKind.Records,
            new ReferenceAttribution(attribution.GetProperty("sourceUrl").GetString()!,
                attribution.GetProperty("author").GetString()!, attribution.GetProperty("license").GetString()!,
                attribution.GetProperty("licenseUrl").GetString()!, new DateOnly(2026, 10, 28),
                attribution.GetProperty("modifications").GetString()!));
        using var stream = Resource("items.json");
        using var data = JsonDocument.Parse(stream);
        return data.RootElement.GetProperty("records").EnumerateArray().ToFrozenDictionary(
            row => int.Parse(row.GetProperty("id").GetString()!, CultureInfo.InvariantCulture),
            row => new ReferenceRecord(row.GetProperty("id").GetString()!, row.GetProperty("name").GetString()!)
            {
                Category = row.GetProperty("category").GetString(),
                Source = row.GetProperty("source").GetString(), Dataset = dataset,
            });
    }

    private static Stream Resource(string name) => typeof(FaithCatalog).Assembly.GetManifestResourceStream(EmbeddedPack + name)
        ?? throw new InvalidOperationException($"Embedded FFXVI reference resource {name} is missing.");
}
