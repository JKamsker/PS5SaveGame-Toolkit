using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise;

/// <summary>Pages across the whole pinned catalog before filtering and deterministic sorting.</summary>
internal static class AriseCatalogQuery
{
    public static IReadOnlyList<QueryableColumn> Columns(string id) =>
    [
        new("id", "Catalog identity", new ReferenceSpec(id) { ReadOnly = true, AllowCustom = true }) { Width = ColumnWidth.Wide },
        new("name", "Name", new TextSpec()),
        new("category", "Category", new TextSpec()),
        new("character", "Character", new TextSpec()),
        new("title", "Title panel", new TextSpec()),
        new("spCost", "SP cost (reference)", new TextSpec()),
        new("state", "Save state", new TextSpec()),
        new("details", "Reference facts and attribution", new TextSpec()) { Width = ColumnWidth.Wide },
        new("inspect", "Inspect", null) { Width = ColumnWidth.Narrow },
    ];

    public static QueryableCollectionPage Query(string id, IReferenceCatalog catalog, QueryableCollectionQuery query)
    {
        var columnKeys = Columns(id).Select(column => column.Key).ToHashSet(StringComparer.Ordinal);
        if (query.Cursor is not null || query.Filters.Keys.Any(key => !columnKeys.Contains(key)) ||
            query.SortColumn is { } sort && !columnKeys.Contains(sort))
        {
            throw new ArgumentException("Use offset paging and published catalog columns for filters and sorting.", nameof(query));
        }

        var matching = All(catalog).Where(record => Matches(record, query)).ToArray();
        var sortColumn = query.SortColumn ?? "id";
        var ordered = query.Descending
            ? matching.OrderByDescending(record => CellText(record, sortColumn), StringComparer.OrdinalIgnoreCase)
            : matching.OrderBy(record => CellText(record, sortColumn), StringComparer.OrdinalIgnoreCase);
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, ReferenceQuery.MaxLimit);
        var rows = ordered.ThenBy(record => record.Key, StringComparer.Ordinal).Skip(offset).Take(limit)
            .Select(record => Row(id, record)).ToArray();
        return new QueryableCollectionPage(id, Columns(id), rows, catalog.Count, matching.Length, offset, limit);
    }

    private static IEnumerable<ReferenceRecord> All(IReferenceCatalog catalog)
    {
        for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
        {
            foreach (var record in catalog.Search(new ReferenceQuery { Offset = offset, Limit = ReferenceQuery.MaxLimit }).Items)
            {
                yield return record;
            }
        }
    }

    private static bool Matches(ReferenceRecord record, QueryableCollectionQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Text) &&
            !string.Join(' ', record.Key, record.Name, record.Category, record.Description,
                string.Join(' ', record.Attributes.Values)).Contains(query.Text, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return query.Filters.All(filter => filter.Key == "character"
            ? CharacterMatches(CellText(record, filter.Key), filter.Value)
            : string.Equals(CellText(record, filter.Key), filter.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool CharacterMatches(string users, string requested) =>
        string.Equals(users, "All", StringComparison.OrdinalIgnoreCase) ||
        users.Split(';', StringSplitOptions.TrimEntries).Contains(requested, StringComparer.OrdinalIgnoreCase);

    private static string CellText(ReferenceRecord record, string column) => column switch
    {
        "id" => record.Key,
        "name" => record.Name,
        "category" => record.Category ?? "Unknown",
        "character" => record.Attributes.GetValueOrDefault("character") ?? "Unknown",
        "title" => record.Attributes.GetValueOrDefault("title") ?? "—",
        "spCost" => record.Attributes.GetValueOrDefault("spCost") ?? "—",
        "state" => "Unknown (not joined to save)",
        "details" => $"{record.Description}\n{record.AttributionText}",
        "inspect" => "Inspect",
        _ => throw new ArgumentException($"Unknown catalog column '{column}'.", nameof(column)),
    };

    private static TableRow Row(string id, ReferenceRecord record)
    {
        var cells = Columns(id).ToDictionary(column => column.Key,
            column => (ViewNode)new TextNode(CellText(record, column.Key)), StringComparer.Ordinal);
        cells["inspect"] = new ActionNode($"references/inspect/{id}", "Inspect")
        {
            Tone = Tone.Info,
            Description = $"Read-only reference details. {record.AttributionText}",
            Parameters = [new FieldNode("record", "Reference record", new ReferenceSpec(id) { ReadOnly = true }, FieldValue.Of(record.Key))],
        };
        cells["id"] = new FieldNode($"catalog/{id}/{record.Key}/id", "Catalog identity",
            new ReferenceSpec(id) { ReadOnly = true, AllowCustom = true }, FieldValue.Of(record.Key))
        {
            Description = $"{record.AttributionText}\nSymbolic keys are reference identities, not serialized save IDs.",
            Confidence = Confidence.Inferred,
        };
        return new TableRow(record.Key, cells);
    }
}
