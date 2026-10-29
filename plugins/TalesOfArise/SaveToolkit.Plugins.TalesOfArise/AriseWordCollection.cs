using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise;

/// <summary>Section-relative evidence, without assigning record identities or gameplay meanings.</summary>
internal sealed class AriseWordCollection(AriseSave save, AriseSection section)
{
    private const int WordSize = sizeof(uint);
    private const int MaximumPageSize = 200;
    private const int CellBufferLength = 32; // UInt32 decimal/hex and section-relative offsets fit.
    public string Id { get; } = $"evidence/{section.Id:X8}";

    private static readonly IReadOnlyList<QueryableColumn> Columns =
    [
        new("offset", "Body offset", new TextSpec()),
        new("hex", "Raw hex", new TextSpec()),
        new("value", "UInt32 (unknown meaning)", IntegerSpec.UInt32 with { ReadOnly = true }),
    ];

    public QueryableCollection Metadata { get; } = new($"evidence/{section.Id:X8}", $"{section.Name} · raw evidence", Columns, section.Size / WordSize)
    {
        Description = "Unknown semantics. Stable keys use schema ID and body offset; nonzero values do not indicate progress or ownership. " + AriseSchema.SectionAttribution,
    };

    public QueryableTableNode View => new(Id, Metadata.Title, Columns, Metadata.TotalCount) { Description = Metadata.Description };

    public QueryableCollectionPage Query(QueryableCollectionQuery query, Func<string, string, int, FieldNode> field)
    {
        if (query.Cursor is not null || query.Filters.Keys.Any(key => !Columns.Any(column => column.Key == key)) ||
            query.SortColumn is { } sort && !Columns.Any(column => column.Key == sort))
        {
            throw new ArgumentException("Use offset paging and published evidence columns.", nameof(query));
        }

        var sortColumn = query.SortColumn ?? "offset";
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, MaximumPageSize);
        var matching = MatchingOffsets(query);
        var count = matching?.Count ?? section.Size / WordSize;
        IEnumerable<int> pageOffsets;
        if (sortColumn == "offset")
        {
            // Offset order is already known. Even a deep, descending unfiltered page
            // only visits the requested words and needs no collection-sized buffer.
            pageOffsets = Enumerable.Range(0, Math.Min(limit, Math.Max(0, count - offset))).Select(index =>
            {
                var position = query.Descending ? count - 1 - offset - index : offset + index;
                return matching is null ? position * WordSize : matching[position];
            });
        }
        else
        {
            IEnumerable<int> candidates = matching is null
                ? Enumerable.Range(0, count).Select(index => index * WordSize) : matching;
            var ordered = query.Descending ? candidates.OrderByDescending(Value) : candidates.OrderBy(Value);
            pageOffsets = ordered.ThenBy(relative => relative).Skip(offset).Take(limit);
        }

        var rows = pageOffsets.Select(relative =>
            new TableRow($"{section.Id:X8}/{relative:X6}", new Dictionary<string, ViewNode>
            {
                ["offset"] = new TextNode(CellText(relative, "offset")),
                ["hex"] = new TextNode(CellText(relative, "hex")),
                ["value"] = field($"{Id}/{relative:X6}", $"{section.Name} +0x{relative:X}", section.Offset + relative)
                    with { Confidence = Confidence.Unknown, Description = Metadata.Description, Warning = null },
            })).ToArray();
        return new QueryableCollectionPage(Id, Columns, rows, Metadata.TotalCount, count, offset, limit);
    }

    private List<int>? MatchingOffsets(QueryableCollectionQuery query)
    {
        // A null list means every word matches, avoiding a collection-sized offset buffer.
        if (string.IsNullOrWhiteSpace(query.Text) && query.Filters.Count == 0) return null;

        var matching = new List<int>();
        var filters = query.Filters.ToArray();
        for (var relative = 0; relative < section.Size; relative += WordSize)
        {
            if (Matches(relative, query.Text, filters)) matching.Add(relative);
        }

        return matching;
    }

    private bool Matches(int relative, string? text, KeyValuePair<string, string>[] filters)
    {
        Span<char> buffer = stackalloc char[CellBufferLength];
        if (!string.IsNullOrWhiteSpace(text))
        {
            var found = false;
            for (var index = 0; index < Columns.Count; index++)
            {
                if (FormatCell(relative, Columns[index].Key, buffer).Contains(text, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }

            if (!found) return false;
        }

        foreach (var filter in filters)
        {
            if (!FormatCell(relative, filter.Key, buffer).Equals(filter.Value, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    private uint Value(int relative) => AriseFormat.U32(save.Payload, section.Offset + relative);

    private string CellText(int relative, string column) => column switch
    {
        "offset" => $"+0x{relative:X6}",
        "hex" => $"0x{Value(relative):X8}",
        "value" => Value(relative).ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentException($"Unknown evidence column '{column}'.", nameof(column)),
    };

    private ReadOnlySpan<char> FormatCell(int relative, string column, Span<char> buffer)
    {
        var prefix = column switch { "offset" => "+0x", "hex" => "0x", _ => "" };
        var format = column switch { "offset" => "X6", "hex" => "X8", _ => "D" };
        prefix.AsSpan().CopyTo(buffer);
        var value = column == "offset" ? (uint)relative : Value(relative);
        value.TryFormat(buffer[prefix.Length..], out var written, format, CultureInfo.InvariantCulture);
        return buffer[..(prefix.Length + written)];
    }
}
