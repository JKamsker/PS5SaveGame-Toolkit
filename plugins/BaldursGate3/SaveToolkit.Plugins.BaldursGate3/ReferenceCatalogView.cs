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
            new TextNode("Pinned, attributed labels are available as bounded, searchable catalogs. Descriptions are original annotations; no icons, game assets or legacy wiki prose are embedded. Raw IDs and unresolved build/module joins remain visible.") { Tone = Tone.Info },
            .. references.Catalogs.Select(c => (ViewNode)new QueryableTableNode(c.Id, c.Dataset.Title ?? c.Id,
                [
                    new QueryableColumn("id", "Raw id", new ReferenceSpec(c.Id) { AllowCustom = true }) { Width = ColumnWidth.Wide },
                    new QueryableColumn("name", "Name") { Width = ColumnWidth.Wide },
                    new QueryableColumn("category", "Category"),
                ], c.Count)
                {
                    Description = $"{c.Dataset.Attribution.ToShortText()}. Search and filter are bounded by the SDK query limits."
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
            Offset = query.Offset,
            Limit = query.Limit,
        };
        var page = query.SortColumn is null
            ? catalog.Search(referenceQuery)
            : SortedPage(catalog, referenceQuery, query.SortColumn, query.Descending);
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
                ["id"] = id,
                ["name"] = new TextNode(record.Name),
                ["category"] = new TextNode(record.Category ?? "Uncategorised"),
            });
        }).ToArray();
        var columns = Collections.Single(c => c.Id == collectionId).Columns;
        return new QueryableCollectionPage(collectionId, columns, rows, catalog.Count, page.Total, page.Offset, query.Limit);
    }

    private static ReferencePage SortedPage(IReferenceCatalog catalog, ReferenceQuery query, string sortColumn, bool descending)
    {
        var all = new List<ReferenceRecord>();
        for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
        {
            var page = catalog.Search(query with { Offset = offset, Limit = ReferenceQuery.MaxLimit });
            all.AddRange(page.Items);
            if (page.Items.Count == 0 || all.Count >= page.Total) break;
        }

        Func<ReferenceRecord, string> key = sortColumn switch
        {
            "id" => record => record.Key,
            "category" => record => record.Category ?? string.Empty,
            "name" => record => record.Name,
            _ => throw new ArgumentException($"Unknown BG3 catalog sort column '{sortColumn}'.", nameof(sortColumn)),
        };
        var ordered = descending ? all.OrderByDescending(key, StringComparer.OrdinalIgnoreCase) : all.OrderBy(key, StringComparer.OrdinalIgnoreCase);
        var records = ordered.ThenBy(record => record.Key, StringComparer.Ordinal).ToArray();
        return new ReferencePage(records.Skip(query.Offset).Take(query.Limit).ToArray(), records.Length, query.Offset);
    }
}
