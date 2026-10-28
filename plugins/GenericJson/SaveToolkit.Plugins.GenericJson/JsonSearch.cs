using System.Text.Json;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>Queries the complete live token tree, independently of expanded UI sections.</summary>
internal sealed class JsonSearch(JsonTokenTree imported)
{
    private const int MaxSortedMatches = 10_000;
    internal const string CollectionId = "json-tree";
    private readonly IReadOnlyDictionary<int, string> _structures = imported.Nodes.Where(n => n.IsContainer)
        .ToDictionary(n => n.Index, n => JsonStructureActions.StructureValue(n).AsString());
    public static IReadOnlyList<QueryableColumn> Columns { get; } = [
        new("path", "Occurrence path") { Width = ColumnWidth.Wide },
        new("type", "JSON type"),
        new("state", "Imported comparison"),
        new("original", "Imported JSON value") { Width = ColumnWidth.Wide },
        new("value", "Current value / edit details") { Width = ColumnWidth.Wide },
        new("within", "Subtree identity"),
        new("reset", "Reset imported values"),
    ];

    public static QueryableCollection Metadata(int count) => new(CollectionId, "Whole-tree search", Columns, count)
    {
        Description = "Search keys, friendly labels, paths, value IDs and live values, including collapsed content. Open details to navigate or edit. "
            + "JSON type and comparison (unchanged, modified, added) filters match exactly. Subtree identity includes descendants. "
            + "Removed entries are in Changes. Select rows to preview reset. Sorting requires at most 10,000 matches.",
    };

    public QueryableCollectionPage Query(JsonValueToken root, QueryableCollectionQuery query,
        Func<JsonValueToken, string, ViewNode> field, Func<JsonValueToken, ViewNode> navigate)
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
                ["state"] = new TextNode(State(entry.Node)),
                ["original"] = new TextNode(Original(entry.Node)) { Monospace = true },
                ["within"] = new TextNode(entry.Node.ValueId) { Monospace = true },
                ["value"] = entry.Node.IsContainer ? new SectionNode($"{entry.Node.Children.Count} entries — open subtree", [navigate(entry.Node)])
                    { Collapsed = true, Description = entry.Path }
                    : new SectionNode(Preview(entry.Node), [field(entry.Node, entry.Path)]),
                ["reset"] = JsonResetActions.View(),
            })).ToArray();
        return new(CollectionId, Columns, page, total, filtered, offset, limit);
    }

    private bool Matches(JsonValueToken node, string path, QueryableCollectionQuery query)
    {
        foreach (var filter in query.Filters)
        {
            var value = SortValue(node, path, filter.Key);
            if (filter.Key == "within")
            {
                if (!Within(node, filter.Value)) return false;
                continue;
            }
            if (filter.Key is "type" or "state" ? !string.Equals(filter.Value, value, StringComparison.OrdinalIgnoreCase)
                : !value.Contains(filter.Value, StringComparison.OrdinalIgnoreCase)) return false;
        }
        var text = query.Text;
        return string.IsNullOrEmpty(text) || node.Label.Contains(text, StringComparison.OrdinalIgnoreCase)
            || path.Contains(text, StringComparison.OrdinalIgnoreCase)
            || node.ValueId.Equals(text, StringComparison.Ordinal)
            || JsonLabels.Friendly(node.Label).Contains(text, StringComparison.OrdinalIgnoreCase)
            || !node.IsContainer && node.Current.ToString().Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private string SortValue(JsonValueToken node, string path, string column) => column switch
    {
        "path" => path,
        "type" => Type(node),
        "state" => State(node),
        "original" => Original(node),
        "within" => node.ValueId,
        "reset" => "Reset existing values to imported values",
        _ => node.IsContainer ? "" : JsonScalar.Literal(node.Current),
    };

    private static string Preview(JsonValueToken node)
    {
        const int MaxPreviewLength = 64;
        var text = JsonScalar.Literal(node.Current).ReplaceLineEndings(" ");
        return $"{Type(node)}: {(text.Length > MaxPreviewLength ? text[..MaxPreviewLength] + "…" : text)}";
    }

    private static string Type(JsonValueToken node) => node.Kind switch
    {
        JsonTokenType.StartObject => "object",
        JsonTokenType.StartArray => "array",
        _ => node.Spec?.ReadOnly == true ? "number" : JsonScalar.TypeOf(node.Current),
    };

    private string Original(JsonValueToken node) => !node.Imported ? "(absent)" : node.IsContainer
        ? _structures[node.Index] : JsonScalar.Literal(node.Original);

    private string State(JsonValueToken node)
    {
        if (!node.Imported) return "added";
        if (node.Parent?.Kind == JsonTokenType.StartObject && node.Label != node.OriginalLabel) return "modified";
        var changed = node.IsContainer ? _structures[node.Index] != JsonStructureActions.StructureValue(node).AsString()
            : node.Current != node.Original;
        return changed ? "modified" : "unchanged";
    }

    private static bool Within(JsonValueToken node, string identity)
    {
        if (identity.Length == 0) return true;
        for (var ancestor = node; ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.ValueId == identity) return true;
        return false;
    }

    private static void Validate(QueryableCollectionQuery query)
    {
        if (query.Cursor is not null) throw new ArgumentException("JSON search uses offset paging.");
        if (query.Text?.Length > QueryableCollectionLimits.MaxQueryLength) throw new ArgumentException("Search text is too long.");
        if (query.SortColumn is { } column && !Columns.Any(c => c.Key == column)) throw new ArgumentException("Unknown sort column.");
        if (query.Filters.Count > Columns.Count || query.Filters.Any(f => !Columns.Any(c => c.Key == f.Key)
            || f.Value.Length > QueryableCollectionLimits.MaxFilterLength)) throw new ArgumentException("Invalid JSON search column filter.");
    }
}
