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
    public string Id => $"evidence/{section.Id:X8}";

    private static readonly IReadOnlyList<QueryableColumn> Columns =
    [
        new("offset", "Body offset", new TextSpec()),
        new("hex", "Raw hex", new TextSpec()),
        new("value", "UInt32 (unknown meaning)", IntegerSpec.UInt32 with { ReadOnly = true }),
    ];

    public QueryableCollection Metadata => new(Id, $"{section.Name} · raw evidence", Columns, section.Size / WordSize)
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

        var matching = Enumerable.Range(0, section.Size / WordSize).Select(index => index * WordSize)
            .Where(relative => Matches(relative, query)).ToArray();
        var sortColumn = query.SortColumn ?? "offset";
        var ordered = sortColumn == "offset"
            ? (query.Descending ? matching.OrderByDescending(relative => relative) : matching.OrderBy(relative => relative))
            : (query.Descending ? matching.OrderByDescending(Value) : matching.OrderBy(Value));
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, MaximumPageSize);
        var rows = ordered.ThenBy(relative => relative).Skip(offset).Take(limit).Select(relative =>
            new TableRow($"{section.Id:X8}/{relative:X6}", new Dictionary<string, ViewNode>
            {
                ["offset"] = new TextNode(CellText(relative, "offset")),
                ["hex"] = new TextNode(CellText(relative, "hex")),
                ["value"] = field($"{Id}/{relative:X6}", $"{section.Name} +0x{relative:X}", section.Offset + relative)
                    with { Confidence = Confidence.Unknown, Description = Metadata.Description, Warning = null },
            })).ToArray();
        return new QueryableCollectionPage(Id, Columns, rows, Metadata.TotalCount, matching.Length, offset, limit);
    }

    private bool Matches(int relative, QueryableCollectionQuery query) =>
        (string.IsNullOrWhiteSpace(query.Text) || Columns.Any(column =>
            CellText(relative, column.Key).Contains(query.Text, StringComparison.OrdinalIgnoreCase))) &&
        query.Filters.All(filter => string.Equals(CellText(relative, filter.Key), filter.Value, StringComparison.OrdinalIgnoreCase));

    private uint Value(int relative) => AriseFormat.U32(save.Payload, section.Offset + relative);

    private string CellText(int relative, string column) => column switch
    {
        "offset" => $"+0x{relative:X6}",
        "hex" => $"0x{Value(relative):X8}",
        "value" => Value(relative).ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentException($"Unknown evidence column '{column}'.", nameof(column)),
    };
}
