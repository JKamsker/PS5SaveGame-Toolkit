using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

/// <summary>Wiki file identities stay separate from writable uint32 save-item identities.</summary>
internal sealed class IllustratedReferences(PragmataDocument document, LazyPages pages)
{
    internal const string CatalogId = "pragmata-illustrated-reference";
    internal const string SectionId = "reference/illustrations";

    public IReadOnlyList<ViewNode> Build() => document.References.Illustrations is { Count: > 0 } catalog
        ? [pages.Section(SectionId, "Illustrated item and equipment reference", catalog.Count,
            (start, count) => Page(catalog, start, count))]
        : [];

    private IReadOnlyList<ViewNode> Page(IReferenceCatalog catalog, int start, int count)
    {
        var rows = catalog.Search(new ReferenceQuery { Offset = start, Limit = count, SortBy = ReferenceSortKey.Name })
            .Items.Select(record => new TableRow($"illustrated/{record.Key}", new Dictionary<string, ViewNode>
            {
                ["icon"] = document.GetAsset(record.AssetId ?? "") is not null && record.AssetId is { } asset
                    ? new ImageNode(asset, $"{record.Name}. {document.References.AssetSource(asset)}")
                    : new TextNode("Image unavailable"),
                ["name"] = document.References.Browse(CatalogId, record),
                ["category"] = new TextNode(record.Category ?? "Unknown"),
                ["details"] = new SectionNode("Description and sources", [
                    new TextNode(record.Description ?? "Description unavailable."),
                    new TextNode(record.AttributionText),
                    new TextNode($"Image source: {document.References.AssetSource(record.AssetId ?? "")}"),
                    new TextNode("Wiki file identity only. The corresponding PS5 item ID, ownership and legal limits are Unknown."),
                ]),
            })).ToArray();
        return [new TextNode("Reference library only: images and descriptions have no proven association with the save's raw item IDs. " +
            "Browsing cannot replace an inventory ID or establish ownership. Official excerpts are wiki transcriptions, not build-verified game data."),
            new TableNode([new("icon", "Image"), new("name", "Reference (browse only)"),
                new("category", "Category"), new("details", "Description / sources")], rows)
                { Title = "Item and equipment image reference" }];
    }
}
