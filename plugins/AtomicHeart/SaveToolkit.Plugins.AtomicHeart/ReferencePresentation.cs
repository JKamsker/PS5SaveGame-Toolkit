using System.Globalization;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart;

/// <summary>Bounded, read-only reference pages. Guide keys never stand in for native save IDs.</summary>
internal sealed class ReferencePresentation(IReferenceContext references)
{
    private const string ItemsPage = "references/items";
    private const string GuidePage = "references/guide";
    private const string SkillsPage = "references/skills";
    private const int PageLimit = ReferenceQuery.MaxLimit;

    internal IReadOnlyList<ViewNode> BuildView() => [
        new TextNode("English reference art and descriptions. Guide costs describe the cited source, not this save's installed upgrades, skill ownership or PS5 build. No verified game caps are available; Amount writes retain their original observed bounds."),
        SectionNode.Lazy("Item catalogue and art", ItemsPage + "/0"),
        SectionNode.Lazy("Resource and module reference", GuidePage + "/0"),
        SectionNode.Lazy("Shok names and costs (community reference)", SkillsPage + "/0"),
    ];

    internal IReadOnlyList<ViewNode> Expand(string lazyId)
    {
        var separator = lazyId.LastIndexOf('/');
        if (separator < 0 || !int.TryParse(lazyId.AsSpan(separator + 1), NumberStyles.None,
                CultureInfo.InvariantCulture, out var offset) || offset < 0)
            throw new KeyNotFoundException(lazyId);
        var prefix = lazyId[..separator];
        var catalogId = prefix switch {
            ItemsPage => InventoryViews.CatalogId, GuidePage => "guide", SkillsPage => "skills",
            _ => throw new KeyNotFoundException(lazyId),
        };
        var catalog = references.Catalog(catalogId);
        if (catalog is null) return [new TextNode("No pinned reference pack. Saved identifiers and evidence views remain available.")];
        var page = catalog.Search(new ReferenceQuery { Offset = offset, Limit = PageLimit });
        if (offset != 0 && offset >= page.Total) throw new KeyNotFoundException(lazyId);
        var nodes = new List<ViewNode> {
            new TextNode(catalog.Dataset.Attribution.ToShortText()),
            new TableNode([new("icon", "Icon"), new("name", "English name"), new("category", "Category"),
                new("description", "Description"), new("facts", "Reference facts"), new("source", "Sources and rights"),
                new("key", "Identifier")], page.Items.Select(Row).ToArray()),
        };
        if (offset + page.Items.Count < page.Total)
            nodes.Add(SectionNode.Lazy("More references", $"{prefix}/{offset + page.Items.Count}"));
        return nodes;
    }

    private TableRow Row(ReferenceRecord record)
    {
        var assetSource = record.AssetId is { } asset ? references.AssetProvenance(asset) : null;
        return new TableRow(record.Key, new Dictionary<string, ViewNode> {
            ["icon"] = Icon(record), ["name"] = new TextNode(record.Name),
            ["category"] = new TextNode(record.Category ?? "Unknown"),
            ["description"] = new TextNode(record.Description ?? "No sourced description."),
            ["facts"] = new TextNode(Attributes(record)),
            ["source"] = new TextNode(record.AttributionText +
                (assetSource is null ? "" : " Art: " + assetSource.Attribution.ToShortText())),
            ["key"] = new TextNode(record.Key.StartsWith("reference/", StringComparison.Ordinal)
                ? record.Key + " (authored reference key; no native save join)" : record.Key),
        });
    }

    internal static ViewNode Icon(ReferenceRecord? record) => record?.AssetId is { } asset
        ? new ImageNode(asset, record.Name + ". " + record.AttributionText)
        : new TextNode("No sourced icon");

    internal static string Attributes(ReferenceRecord? record) => record is null ? "" :
        string.Join("; ", record.Attributes.Select(pair => $"{pair.Key}: {pair.Value}"));
}
