using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>Bounded guide lists with browse-only catalog fields; wiki identities never become save IDs.</summary>
internal sealed class TsushimaGuideViews(IReferenceContext references, Func<FieldNode, FieldNode> bind)
{
    private const int GuidePageSize = ReferenceQuery.MaxLimit;
    private const string GuideWarning = "Reference only: wiki keys are not save IDs. Ownership, equipped, " +
        "discovered, completed and rewarded state remain Unknown.";

    public TableNode WorldTable()
    {
        var catalog = references.Catalog(TsushimaIds.WorldGuide);
        return catalog is null
            ? new TableNode(Columns(), []) { Title = "Shrine reference", Description = "No pinned reference guide is available." }
            : Table(catalog);
    }

    public IReadOnlyList<ViewNode> Expand(string lazyId)
    {
        var catalogId = lazyId[TsushimaIds.GuidePrefix.Length..];
        if (catalogId is not (TsushimaIds.ResourceGuide or TsushimaIds.CharmGuide or TsushimaIds.ArmorGuide or
            TsushimaIds.SwordGuide or TsushimaIds.TechniqueGuide or TsushimaIds.WorldGuide))
        {
            throw new KeyNotFoundException($"Unknown reference guide '{lazyId}'.");
        }

        var catalog = references.Catalog(catalogId);
        if (catalog is null)
        {
            return [new TextNode("No pinned reference guide is available. Save identifiers remain unchanged.")];
        }

        var table = Table(catalog);
        return [new TextNode($"{GuideWarning} Showing {table.Rows.Count} of {catalog.Count} entries. " +
            "Open a name to browse the full pinned catalog with search, category filters and sorting."), table];
    }

    private TableNode Table(IReferenceCatalog catalog)
    {
        // Use the shared catalog ordering; custom catalogs may declare no sort support.
        var page = catalog.Search(new ReferenceQuery
        {
            Limit = GuidePageSize,
            SortBy = catalog.SortKeys.Contains(ReferenceSortKey.Category) ? ReferenceSortKey.Category : null,
        });
        var rows = page.Items.Select(record => new TableRow(record.Key, new Dictionary<string, ViewNode>
        {
            ["name"] = bind(new FieldNode($"guides/{catalog.Id}/{record.Key}/reference", record.Name,
                new ReferenceSpec(catalog.Id) { ReadOnly = true }, FieldValue.Of(record.Key))
            {
                Confidence = Confidence.Unknown,
                Description = $"{record.Description} {record.AttributionText}",
                Warning = GuideWarning,
            }),
            ["category"] = new TextNode(record.Category ?? "Unknown"),
            ["description"] = new TextNode(record.Description ?? ""),
            ["state"] = new TextNode("Unmapped; save state Unknown") { Tone = Tone.Warning },
            ["source"] = new TextNode(record.AttributionText),
        })).ToArray();
        return new TableNode(Columns(), rows)
        {
            Title = catalog.Dataset.Title,
            Description = catalog.Dataset.Attribution.ToShortText(),
        };
    }

    private static IReadOnlyList<TableColumn> Columns() =>
    [new("name", "Name"), new("category", "Category / region"), new("description", "Description"),
        new("state", "Save mapping / state"), new("source", "Attribution")];
}
