using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas;

/// <summary>Message-only inspection of pinned catalogues, independent of save editability.</summary>
internal sealed class CatalogueInspection(IReferenceContext references, params string[] catalogIds)
{
    private const string ActionPrefix = "unrealgvas/catalogue/inspect/";
    private const int CategoryPreviewLimit = 10;
    private readonly IReadOnlyList<IReferenceCatalog> _catalogues = catalogIds.Distinct(StringComparer.Ordinal)
        .Select(id => references.Catalog(id)).OfType<IReferenceCatalog>().ToArray();

    public IReadOnlyList<ViewNode> Build(Func<string, IReferenceCatalog, ReferenceRecord, FieldNode> bind) =>
        _catalogues.SelectMany(catalogue =>
        {
            var first = catalogue.Search(new ReferenceQuery { Limit = 1 }).Items.FirstOrDefault();
            var nodes = new List<ViewNode>();
            if (first is not null)
            {
                if (first.AssetId is { } asset && references.GetAsset(asset) is not null && references.AssetProvenance(asset) is not null)
                    nodes.Add(new ImageNode(asset, first.Name + ". " + first.AttributionText));
                nodes.Add(bind("catalogue/" + catalogue.Id, catalogue, first) with
                {
                    Confidence = Confidence.Inferred,
                    Description = first.AttributionText,
                    Warning = "Catalogue browsing only. This ID is a catalogue example, not a saved value or evidence of ownership.",
                });
            }
            nodes.Add(new ActionNode(ActionPrefix + catalogue.Id, "Inspect " + (catalogue.Dataset.Title ?? catalogue.Id))
            {
                Mutates = false,
                Description = "Report pinned catalogue coverage and provenance without changing the save. " + catalogue.Dataset.Attribution.ToShortText(),
            });
            return nodes;
        }).ToArray();

    public ActionOutcome? Invoke(string actionId)
    {
        var catalogue = _catalogues.FirstOrDefault(catalogue => actionId == ActionPrefix + catalogue.Id);
        if (catalogue is null) return null;
        string categories = string.Join(", ", catalogue.Categories.Take(CategoryPreviewLimit));
        if (catalogue.Categories.Count > CategoryPreviewLimit) categories += ", …";
        return new ActionOutcome
        {
            Message = $"{catalogue.Dataset.Title ?? catalogue.Id}: {catalogue.Count:N0} reference records; {catalogue.Categories.Count:N0} categories ({categories}). " +
                $"Declared sorting: {string.Join(", ", catalogue.SortKeys)}. " +
                "Catalogue records do not establish ownership, discovery or game state. " + catalogue.Dataset.Attribution.ToShortText(),
        };
    }
}
