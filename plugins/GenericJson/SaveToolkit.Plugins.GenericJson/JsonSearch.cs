using System.Text.Json;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>Queries the complete live token tree, independently of expanded UI sections.</summary>
internal sealed class JsonSearch(JsonTokenTree imported)
{
    private const int MaxSortedMatches = 10_000;
    internal const string CollectionId = "json-tree";
    internal const int ArrayTableThreshold = 100;
    internal const string ArrayPrefix = "json-array-";
    private const int MaxDisplayLiteral = 4096;
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

    public static QueryableCollection Metadata(int count, string id = CollectionId) => new(id,
        id == CollectionId ? "Whole-tree search" : "Array entries", Columns, count)
    {
        Description = "Search keys, friendly labels, paths, value IDs and live values, including collapsed content. Open details to navigate or edit. "
            + "JSON type and comparison (unchanged, modified, added) filters match exactly. Subtree identity includes descendants. "
            + "Removed entries are in Changes. Select rows to preview reset. Sorting requires at most 10,000 matches.",
    };

    public QueryableCollectionPage Query(JsonTokenTree tree, JsonValueToken root, QueryableCollectionQuery query,
        Func<JsonValueToken, string, ViewNode> field, Func<JsonValueToken, ViewNode> navigate, string collectionId = CollectionId)
    {
        Validate(query);
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        var rows = new List<JsonValueToken>();
        long total = 0, filtered = 0;
        var sort = query.SortColumn is not null || query.Descending;
        var filters = query.Filters.ToArray();
        var entries = collectionId == CollectionId ? tree.Nodes : root.Children;
        if (!sort && string.IsNullOrEmpty(query.Text) && query.Filters.Count == 0)
        {
            total = filtered = entries.Count;
            rows.AddRange(entries.Skip(offset).Take(limit));
        }
        else
        {
            foreach (var entry in entries)
            {
                total++;
                if (!Matches(entry, query.Text, filters)) continue;
                if (sort || filtered >= offset && filtered - offset < limit) rows.Add(entry);
                filtered++;
                if (sort && rows.Count > MaxSortedMatches)
                    throw new ArgumentException("Narrow the search to at most 10,000 matches before sorting.");
            }
        }
        if (query.SortColumn is { } column)
        {
            // Compute expensive scalar/structure literals once per match, not once per comparison.
            var keys = rows.ToDictionary(row => row.Index, row => SortValue(row, column));
            rows.Sort((a, b) =>
            {
                var comparison = string.CompareOrdinal(keys[a.Index], keys[b.Index]);
                if (comparison == 0) comparison = a.Index.CompareTo(b.Index);
                return query.Descending ? -comparison : comparison;
            });
        }
        else if (query.Descending) rows.Reverse();
        var page = rows.Skip(sort ? offset : 0).Take(limit).Select(node =>
        {
            var path = JsonPaths.ForNode(node);
            return new TableRow(node.ValueId,
            new Dictionary<string, ViewNode>(StringComparer.Ordinal)
            {
                ["path"] = new TextNode(path) { Monospace = true },
                ["type"] = new TextNode(Type(node)),
                ["state"] = new TextNode(State(node)),
                ["original"] = new TextNode(Clip(Original(node))) { Monospace = true },
                ["within"] = new TextNode(node.ValueId) { Monospace = true },
                ["value"] = node.IsContainer ? new SectionNode($"{node.Children.Count} entries — open subtree", [navigate(node)])
                    { Collapsed = true, Description = path }
                    : new SectionNode(Preview(node), [field(node, path)]),
                ["reset"] = JsonResetActions.View(),
            });
        }).ToArray();
        return new(collectionId, Columns, page, total, filtered, offset, limit);
    }

    public static bool ValidScope(JsonTokenTree tree, string id) => id == CollectionId || Array(tree, id) is not null;

    public static JsonValueToken? Array(JsonTokenTree tree, string id) => id.StartsWith(ArrayPrefix, StringComparison.Ordinal)
        && tree.FindValue("value/" + id[ArrayPrefix.Length..]) is { Kind: JsonTokenType.StartArray } node
        && node.Children.Count > ArrayTableThreshold ? node : null;

    private static string Clip(string text) => text.Length <= MaxDisplayLiteral ? text
        : text[..MaxDisplayLiteral] + "… (complete value in raw JSON)";

    private bool Matches(JsonValueToken node, string? text, KeyValuePair<string, string>[] filters)
    {
        foreach (var filter in filters)
        {
            if (filter.Key == "within")
            {
                if (!Within(node, filter.Value)) return false;
                continue;
            }
            if (filter.Key == "path")
            {
                if (!JsonPaths.Contains(node, filter.Value)) return false;
                continue;
            }
            var value = SortValue(node, filter.Key);
            if (filter.Key is "type" or "state" ? !string.Equals(filter.Value, value, StringComparison.OrdinalIgnoreCase)
                : !value.Contains(filter.Value, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return string.IsNullOrEmpty(text) || node.Label.Contains(text, StringComparison.OrdinalIgnoreCase)
            || node.ValueId.Equals(text, StringComparison.Ordinal)
            || (node.Parent?.Kind == JsonTokenType.StartArray ? node.Label : node.FriendlyLabel ??= JsonLabels.Friendly(node.Label))
                .Contains(text, StringComparison.OrdinalIgnoreCase)
            || !node.IsContainer && node.Current.ToString().Contains(text, StringComparison.OrdinalIgnoreCase)
            || JsonPaths.Contains(node, text);
    }

    private string SortValue(JsonValueToken node, string column) => column switch
    {
        "path" => JsonPaths.ForNode(node),
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
        var changed = node.IsContainer ? !OriginalStructure(node)
            : node.Current != node.Original;
        return changed ? "modified" : "unchanged";
    }

    private static bool OriginalStructure(JsonValueToken node)
    {
        if (node.Children.Count != node.OriginalChildIds.Count) return false;
        for (var i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            if (child.Index != node.OriginalChildIds[i]
                || node.Kind == JsonTokenType.StartObject && child.Label != child.OriginalLabel) return false;
        }
        return true;
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
