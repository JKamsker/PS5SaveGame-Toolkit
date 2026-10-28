using System.Text.Json;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>Queries the complete live token tree, independently of expanded UI sections.</summary>
internal static class JsonSearch
{
    private const int MaxSortedMatches = 10_000;
    internal const string CollectionId = "json-tree";
    public static IReadOnlyList<QueryableColumn> Columns { get; } = [
        new("path", "Occurrence path") { Width = ColumnWidth.Wide },
        new("type", "JSON type"),
        new("value", "Current value / edit details") { Width = ColumnWidth.Wide },
        new("reset", "Reset imported values"),
    ];

    public static QueryableCollection Metadata(int count) => new(CollectionId, "Whole-tree search", Columns, count)
    {
        Description = "Search all keys, occurrence paths and live scalar values, including collapsed content. "
            + "Open value details to edit a match; select rows before previewing a reset. Type filter is exact: object, array, null, string, number, boolean. Other column filters match text. "
            + "Sorting is lexical and requires at most 10,000 matches; narrow larger queries. Long displayed paths are clipped and retain the node ID.",
    };

    public static QueryableCollectionPage Query(JsonValueToken root, QueryableCollectionQuery query,
        Func<JsonValueToken, string, ViewNode> field)
    {
        Validate(query);
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        var rows = new List<(JsonValueToken Node, string Path)>();
        long total = 0, filtered = 0;
        var sort = query.SortColumn is not null || query.Descending;
        foreach (var entry in JsonPaths.Walk(root))
        {
            total++;
            if (!Matches(entry.Node, entry.Path, query)) continue;
            if (sort || filtered >= offset && filtered - offset < limit) rows.Add(entry);
            filtered++;
            if (sort && rows.Count > MaxSortedMatches)
                throw new ArgumentException("Narrow the search to at most 10,000 matches before sorting.");
        }
        if (query.SortColumn is { } column)
        {
            rows.Sort((a, b) =>
            {
                var comparison = string.CompareOrdinal(SortValue(a.Node, a.Path, column), SortValue(b.Node, b.Path, column));
                if (comparison == 0) comparison = a.Node.Index.CompareTo(b.Node.Index);
                return query.Descending ? -comparison : comparison;
            });
        }
        else if (query.Descending) rows.Reverse();
        var page = rows.Skip(sort ? offset : 0).Take(limit).Select(entry => new TableRow(entry.Node.ValueId,
            new Dictionary<string, ViewNode>(StringComparer.Ordinal)
            {
                ["path"] = new TextNode(entry.Path) { Monospace = true },
                ["type"] = new TextNode(Type(entry.Node)),
                ["value"] = entry.Node.IsContainer ? new TextNode($"{entry.Node.Children.Count} entries")
                    : new SectionNode(Preview(entry.Node), [field(entry.Node, entry.Path)]),
                ["reset"] = JsonResetActions.View(),
            })).ToArray();
        return new(CollectionId, Columns, page, total, filtered, offset, limit);
    }

    private static bool Matches(JsonValueToken node, string path, QueryableCollectionQuery query)
    {
        foreach (var filter in query.Filters)
        {
            var value = SortValue(node, path, filter.Key);
            if (filter.Key == "type" ? !string.Equals(filter.Value, value, StringComparison.OrdinalIgnoreCase)
                : !value.Contains(filter.Value, StringComparison.OrdinalIgnoreCase)) return false;
        }
        var text = query.Text;
        return string.IsNullOrEmpty(text) || node.Label.Contains(text, StringComparison.OrdinalIgnoreCase)
            || path.Contains(text, StringComparison.OrdinalIgnoreCase)
            || !node.IsContainer && node.Current.ToString().Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static string SortValue(JsonValueToken node, string path, string column) => column switch
    {
        "path" => path,
        "type" => Type(node),
        "reset" => "Reset existing values to imported values",
        _ => node.IsContainer ? "" : node.Current.ToString(),
    };

    private static string Preview(JsonValueToken node)
    {
        const int MaxPreviewLength = 64;
        var text = node.Current.ToString().ReplaceLineEndings(" ");
        return $"{Type(node)}: {(text.Length > MaxPreviewLength ? text[..MaxPreviewLength] + "…" : text)}";
    }

    private static string Type(JsonValueToken node) => node.Kind switch
    {
        JsonTokenType.StartObject => "object",
        JsonTokenType.StartArray => "array",
        _ => node.Spec?.ReadOnly == true ? "number" : JsonScalar.TypeOf(node.Current),
    };

    private static void Validate(QueryableCollectionQuery query)
    {
        if (query.Cursor is not null) throw new ArgumentException("JSON search uses offset paging.");
        if (query.Text?.Length > QueryableCollectionLimits.MaxQueryLength) throw new ArgumentException("Search text is too long.");
        if (query.SortColumn is not null and not ("path" or "type" or "value" or "reset")) throw new ArgumentException("Unknown sort column.");
        if (query.Filters.Count > Columns.Count || query.Filters.Any(f => f.Key is not ("path" or "type" or "value" or "reset")
            || f.Value.Length > QueryableCollectionLimits.MaxFilterLength)) throw new ArgumentException("Invalid JSON search column filter.");
    }
}
