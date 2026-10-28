using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart;

/// <summary>Searches the complete immutable record index before paging; no eager property views.</summary>
internal sealed class RecordCollection(RecordIndex index)
{
    internal const string Id = "atomic-heart.records";
    private const int MaxPageSize = 200;
    private static IReadOnlyList<QueryableColumn> Columns => [
        new("kind", "Kind", new TextSpec()), new("name", "Name", new TextSpec()),
        new("owner", "Owner / level", new TextSpec()), new("class", "Class path", new TextSpec()),
        new("offset", "Decoded offset", IntegerSpec.Int32), new("bytes", "Record bytes", IntegerSpec.Int32),
        new("details", "Properties and bytes"),
    ];

    internal QueryableCollection Info => new(Id, "Search all records", Columns, index.Objects.Count);
    internal QueryableTableNode View => new(Id, Info.Title, Columns, index.Objects.Count) {
        Description = "Search covers all objects, including unloaded pages. Filter kind by actor, component or subsystem. Names and class paths are raw identifiers.",
    };

    internal QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        if (collectionId != Id) throw new KeyNotFoundException(collectionId);
        if (query.Cursor is not null) throw new ArgumentException("Use offset paging for the record index.", nameof(query));
        var entries = index.Objects.Select((record, ordinal) => (Record: record, Ordinal: ordinal));
        if (!string.IsNullOrWhiteSpace(query.Text)) entries = entries.Where(e =>
            e.Record.Name.Contains(query.Text, StringComparison.OrdinalIgnoreCase) ||
            e.Record.Owner.Contains(query.Text, StringComparison.OrdinalIgnoreCase) ||
            e.Record.ClassPath.Contains(query.Text, StringComparison.OrdinalIgnoreCase) ||
            e.Record.ClassSubPath.Contains(query.Text, StringComparison.OrdinalIgnoreCase));
        foreach (var (column, value) in query.Filters)
        {
            ValidateColumn(column);
            entries = entries.Where(e => Text(e.Record, column).Equals(value, StringComparison.OrdinalIgnoreCase));
        }
        var filtered = entries.ToArray();
        var sort = query.SortColumn ?? "offset";
        ValidateColumn(sort);
        IOrderedEnumerable<(ObjectRecord Record, int Ordinal)> ordered;
        if (sort is "offset" or "bytes")
        {
            ordered = query.Descending ? filtered.OrderByDescending(e => Number(e.Record, sort)) : filtered.OrderBy(e => Number(e.Record, sort));
        }
        else
        {
            ordered = query.Descending ? filtered.OrderByDescending(e => Text(e.Record, sort), StringComparer.OrdinalIgnoreCase) :
                filtered.OrderBy(e => Text(e.Record, sort), StringComparer.OrdinalIgnoreCase);
        }
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, MaxPageSize);
        var rows = ordered.ThenBy(e => e.Record.Id, StringComparer.Ordinal).Skip(offset).Take(limit).Select(e => Row(e.Record, e.Ordinal)).ToArray();
        return new QueryableCollectionPage(Id, Columns, rows, index.Objects.Count, filtered.Length, offset, limit);
    }

    private static TableRow Row(ObjectRecord record, int ordinal) => new(record.Id, new Dictionary<string, ViewNode> {
        ["kind"] = new TextNode(record.Kind), ["name"] = new TextNode(record.Name), ["owner"] = new TextNode(record.Owner),
        ["class"] = new TextNode(Text(record, "class")),
        ["offset"] = new TextNode(record.Span.Offset.ToString()), ["bytes"] = new TextNode(record.Span.Length.ToString()),
        ["details"] = SectionNode.Lazy("Inspect record", $"inspect/object/{ordinal}"),
    });

    private static string Text(ObjectRecord record, string column) => column switch {
        "kind" => record.Kind, "name" => record.Name, "owner" => record.Owner,
        "class" => record.ClassPath + (record.ClassSubPath.Length == 0 ? "" : $" | {record.ClassSubPath}"),
        "offset" => record.Span.Offset.ToString(), "bytes" => record.Span.Length.ToString(),
        _ => throw new ArgumentException($"Unknown record column '{column}'.", nameof(column)),
    };
    private static int Number(ObjectRecord record, string column) => column == "offset" ? record.Span.Offset : record.Span.Length;
    private static void ValidateColumn(string column)
    {
        if (column is not ("kind" or "name" or "owner" or "class" or "offset" or "bytes"))
            throw new ArgumentException($"Unknown record column '{column}'.", nameof(column));
    }
}
