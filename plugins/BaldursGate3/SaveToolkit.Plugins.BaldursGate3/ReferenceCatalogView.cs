using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Read-only catalog browsing backed by the SDK's bounded F8 query contract.</summary>
internal sealed class ReferenceCatalogView(Bg3References references)
{
    internal IReadOnlyList<QueryableCollection> Collections => references.Catalogs.Select(c =>
        new QueryableCollection(c.Id, c.Dataset.Title ?? c.Id,
        [
            new QueryableColumn("icon", "Icon"),
            new QueryableColumn("id", "Raw id", new ReferenceSpec(c.Id) { AllowCustom = true }) { Width = ColumnWidth.Wide },
            new QueryableColumn("name", "Name") { Width = ColumnWidth.Wide },
            new QueryableColumn("category", "Category"),
        ], c.Count)
        {
            Description = $"{c.Dataset.Attribution.ToShortText()}. The catalog is a candidate label set; it does not prove a save identity."
        }).ToArray();

    internal IReadOnlyList<ViewNode> Build()
    {
        if (references.Catalogs.Count == 0)
        {
            return [new TextNode("No attributed BG3 reference pack is loaded; raw identifiers remain visible.") { Tone = Tone.Info }];
        }

        return
        [
            new TextNode("Pinned, attributed catalogs include local Larian game icons and short game-tooltip/flavour-text excerpts via bg3.wiki. Sources retain separate game-asset and CC BY-SA label attribution. Raw IDs and unresolved build/module joins remain visible.") { Tone = Tone.Info },
            .. Collections.Select(c => (ViewNode)new QueryableTableNode(c.Id, c.Title, c.Columns, c.TotalCount)
                {
                    Description = c.Description,
                }),
        ];
    }

    internal QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query,
        Func<string, string, FieldValue, FieldNode> bind)
    {
        var catalog = references.Catalog(collectionId) ?? throw new KeyNotFoundException($"Unknown BG3 catalog '{collectionId}'.");
        var referenceQuery = new ReferenceQuery
        {
            Text = query.Text,
            Category = query.Filters.GetValueOrDefault("category"),
            SortBy = query.SortColumn switch
            {
                null => null,
                "id" => ReferenceSortKey.Key,
                "name" or "icon" => ReferenceSortKey.Name, // An icon sorts by its associated name.
                "category" => ReferenceSortKey.Category,
                _ => throw new ArgumentException($"Unknown BG3 catalog sort column '{query.SortColumn}'.", nameof(query)),
            },
            Descending = query.Descending,
            Offset = query.Offset,
            Limit = query.Limit,
        };
        var page = catalog.Search(referenceQuery);
        var rows = page.Items.Select(record =>
        {
            var key = $"catalog/{collectionId}/{Uri.EscapeDataString(record.Key)}";
            var id = bind($"{key}/id", "Raw id", FieldValue.Of(record.Key)) with
            {
                Spec = new ReferenceSpec(collectionId) { AllowCustom = true, ReadOnly = true },
                Confidence = Confidence.Inferred,
                Description = $"{record.AttributionText}. Candidate catalog key; no live save join is established.",
            };
            return new TableRow(key, new Dictionary<string, ViewNode>
            {
                ["icon"] = references.Icon(record),
                ["id"] = id,
                ["name"] = new SectionNode(record.Name,
                    [new TextNode(record.Description ?? "No description available."), new TextNode(record.AttributionText)]),
                ["category"] = new TextNode(record.Category ?? "Uncategorised"),
            });
        }).ToArray();
        var columns = Collections.Single(c => c.Id == collectionId).Columns;
        return new QueryableCollectionPage(collectionId, columns, rows, catalog.Count, page.Total, page.Offset, query.Limit);
    }

}
