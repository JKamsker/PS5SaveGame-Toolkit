using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart;

/// <summary>Searches the complete immutable record index before paging; no eager property views.</summary>
internal sealed class RecordCollection(RecordIndex index)
{
    internal const string Id = "atomic-heart.records";
    private const int MaxPageSize = 200;
    private (ObjectRecord Record, int Ordinal)[]? cachedEntries;
    private QueryableCollectionQuery? cachedQuery;
    private string?[]? classLabels;
    private static readonly IReadOnlyList<QueryableColumn> Columns = [
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
        var sort = query.SortColumn ?? "offset";
        ValidateColumn(sort);
        foreach (var column in query.Filters.Keys) ValidateColumn(column);
        if (cachedQuery is null || !MatchesCriteria(query, cachedQuery))
        {
            var entries = Select(query, sort);
            // The caller may reuse and mutate its filter dictionary. Retain our own snapshot;
            // offset/limit/empty visibility do not affect this immutable record collection.
            cachedQuery = query with { Filters = new Dictionary<string, string>(query.Filters, StringComparer.Ordinal) };
            cachedEntries = entries;
        }
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, MaxPageSize);
        var filtered = cachedEntries!;
        var rows = filtered.Skip(offset).Take(limit).Select(e => Row(e.Record, e.Ordinal)).ToArray();
        return new QueryableCollectionPage(Id, Columns, rows, index.Objects.Count, filtered.Length, offset, limit);
    }

    private (ObjectRecord Record, int Ordinal)[] Select(QueryableCollectionQuery query, string sort)
    {
        var entries = index.Objects.Select((record, ordinal) => (Record: record, Ordinal: ordinal));
        if (!string.IsNullOrWhiteSpace(query.Text)) entries = entries.Where(e =>
            e.Record.Name.Contains(query.Text, StringComparison.OrdinalIgnoreCase) ||
            e.Record.Owner.Contains(query.Text, StringComparison.OrdinalIgnoreCase) ||
            e.Record.ClassPath.Contains(query.Text, StringComparison.OrdinalIgnoreCase) ||
            e.Record.ClassSubPath.Contains(query.Text, StringComparison.OrdinalIgnoreCase));
        foreach (var (column, value) in query.Filters)
        {
            entries = entries.Where(e => MatchesFilter(e, column, value));
        }
        IOrderedEnumerable<(ObjectRecord Record, int Ordinal)> ordered;
        if (sort is "offset" or "bytes")
        {
            ordered = query.Descending ? entries.OrderByDescending(e => Number(e.Record, sort)) : entries.OrderBy(e => Number(e.Record, sort));
        }
        else
        {
            ordered = query.Descending ? entries.OrderByDescending(e => Text(e, sort), StringComparer.OrdinalIgnoreCase) :
                entries.OrderBy(e => Text(e, sort), StringComparer.OrdinalIgnoreCase);
        }
        return ordered.ThenBy(e => e.Record.Id, StringComparer.Ordinal).ToArray();
    }

    private static bool MatchesCriteria(QueryableCollectionQuery query, QueryableCollectionQuery cached) =>
        query.Text == cached.Text && query.SortColumn == cached.SortColumn && query.Descending == cached.Descending &&
        query.Filters.Count == cached.Filters.Count && query.Filters.All(pair =>
            cached.Filters.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private bool MatchesFilter((ObjectRecord Record, int Ordinal) entry, string column, string value)
    {
        if (column is not ("offset" or "bytes")) return Text(entry, column).Equals(value, StringComparison.OrdinalIgnoreCase);
        Span<char> buffer = stackalloc char[32];
        return Number(entry.Record, column).TryFormat(buffer, out var written) &&
            buffer[..written].Equals(value.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private TableRow Row(ObjectRecord record, int ordinal) => new(record.Id, new Dictionary<string, ViewNode> {
        ["kind"] = new TextNode(record.Kind), ["name"] = new TextNode(record.Name), ["owner"] = new TextNode(record.Owner),
        ["class"] = new TextNode(Text((record, ordinal), "class")),
        ["offset"] = new TextNode(record.Span.Offset.ToString()), ["bytes"] = new TextNode(record.Span.Length.ToString()),
        ["details"] = SectionNode.Lazy("Inspect record", $"inspect/object/{ordinal}"),
    });

    private string Text((ObjectRecord Record, int Ordinal) entry, string column) => column switch {
        "kind" => entry.Record.Kind, "name" => entry.Record.Name, "owner" => entry.Record.Owner,
        "class" => ClassLabel(entry.Record, entry.Ordinal),
        _ => throw new ArgumentException($"Unknown record column '{column}'.", nameof(column)),
    };

    private string ClassLabel(ObjectRecord record, int ordinal)
    {
        if (record.ClassSubPath.Length == 0) return record.ClassPath;
        classLabels ??= new string?[index.Objects.Count];
        return classLabels[ordinal] ??= record.ClassPath + " | " + record.ClassSubPath;
    }
    private static int Number(ObjectRecord record, string column) => column == "offset" ? record.Span.Offset : record.Span.Length;
    private static void ValidateColumn(string column)
    {
        if (column is not ("kind" or "name" or "owner" or "class" or "offset" or "bytes"))
            throw new ArgumentException($"Unknown record column '{column}'.", nameof(column));
    }
}
