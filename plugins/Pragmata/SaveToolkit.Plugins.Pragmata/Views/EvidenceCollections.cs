using System.Globalization;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

/// <summary>Queries stored records without constructing their detail views until a page is requested.</summary>
internal sealed class EvidenceCollections(PragmataDocument document)
{
    private const int MaximumSummaryFields = 12;
    private static readonly QueryableColumn[] Columns =
    [
        new("identity", "Stored identity", new TextSpec()),
        new("type", "Stored type", new TextSpec()),
        new("values", "Stored values (meaning unknown)", new TextSpec()),
        new("details", "Existing values / sources"),
    ];
    private readonly Dictionary<string, Collection> _collections = new(StringComparer.Ordinal);
    public IReadOnlyList<QueryableCollection> Metadata => _collections.Values.Select(c => c.Metadata).ToArray();

    public QueryableTableNode Table(SaveNode array, Func<SaveNode, ViewNode> details)
    {
        // Arrays cannot move or resize in this fixed-size editor. Offsets identify physical records,
        // including duplicate IDs, without guessing which stored flags are gameplay identities.
        var id = $"evidence/array-at-{array.Offset:x}";
        var metadata = new QueryableCollection(id, HashNames.Label(array.Hash), Columns, array.Children.Count)
        {
            Description = $"Identifier names: {HashNames.Attribution} Counts are stored records, not completion totals. " +
                $"Search covers raw IDs, types and the first {MaximumSummaryFields} direct stored fields. Gameplay meanings are Unknown. " +
                "Row keys identify fixed plaintext offsets; they do not establish a gameplay identity.",
        };
        _collections[id] = new Collection(metadata, array, details);
        return new QueryableTableNode(id, metadata.Title, Columns, metadata.TotalCount) { Description = metadata.Description };
    }

    public QueryableCollectionPage Query(string id, QueryableCollectionQuery query)
    {
        if (!_collections.TryGetValue(id, out var collection)) throw new KeyNotFoundException($"Unknown Pragmata collection '{id}'.");
        Validate(query);
        // Read live values: raw edits must immediately affect filters and sorting after a view rebuild.
        var rows = collection.Array.Children.Select(Describe);
        if (!string.IsNullOrWhiteSpace(query.Text))
            rows = rows.Where(row => row.Search.Contains(query.Text, StringComparison.OrdinalIgnoreCase));
        foreach (var (column, value) in query.Filters)
            rows = rows.Where(row => string.Equals(row.Cell(column), value, StringComparison.OrdinalIgnoreCase));
        var sort = query.SortColumn ?? "identity";
        var ordered = query.Descending
            ? rows.OrderByDescending(row => row.Cell(sort), StringComparer.Ordinal)
            : rows.OrderBy(row => row.Cell(sort), StringComparer.Ordinal);
        var filtered = ordered.ThenBy(row => row.Key, StringComparer.Ordinal).ToArray();
        var page = filtered.Skip(query.Offset).Take(query.Limit).Select(row => new TableRow(row.Key,
            new Dictionary<string, ViewNode>
            {
                ["identity"] = new TextNode(row.Identity) { Monospace = true },
                ["type"] = new TextNode(row.Type),
                ["values"] = new TextNode(row.Values),
                ["details"] = collection.Details(row.Node),
            })).ToArray();
        return new QueryableCollectionPage(id, Columns, page, collection.Array.Children.Count, filtered.Length, query.Offset, query.Limit);
    }

    private Row Describe(SaveNode node)
    {
        var fields = node.Type == FieldTags.Class ? node.Children : [node];
        var values = string.Join("; ", fields.Take(MaximumSummaryFields).Select(field =>
            $"{HashNames.Label(field.Hash)}={Value(field)}"));
        if (fields.Count > MaximumSummaryFields) values += "; … (open details for remaining fields)";
        var identity = fields.FirstOrDefault(field => HashNames.All.GetValueOrDefault(field.Hash) is
            "_ID" or "_Id" or "ID" or "Id" or "ChapterIDHash" or "_EventType" or "_RecordType");
        var label = identity is null ? $"plaintext @0x{node.Offset:x}" : $"{HashNames.Label(identity.Hash)}={Value(identity)}";
        return new Row(node, $"record-at-{node.Offset:x}", label,
            node.Type == FieldTags.Class ? HashNames.Label(node.ClassHash) : FieldTags.ShortName(node.Type), values);
    }

    private string Value(SaveNode node) => node.Type switch
    {
        FieldTags.Array => $"{node.Children.Count.ToString(CultureInfo.InvariantCulture)} stored records",
        FieldTags.Class => HashNames.Label(node.ClassHash),
        FieldTags.Struct => document.Fingerprint(node),
        FieldTags.String => "Text (open details)",
        FieldTags.UInt32 => $"{document.Read(node)} / 0x{document.Read(node).AsUInt32():x8}",
        _ => document.Read(node).ToString(),
    };

    private static void Validate(QueryableCollectionQuery query)
    {
        if (query.Limit is < 1 or > QueryableCollectionLimits.MaxPageSize || query.Offset < 0)
            throw new ArgumentOutOfRangeException(nameof(query), "Invalid page size or offset.");
        if (query.Cursor is not null) throw new ArgumentException("Use offset paging for Pragmata evidence collections.", nameof(query));
        if (query.Text?.Length > QueryableCollectionLimits.MaxQueryLength || query.Filters.Count > QueryableCollectionLimits.MaxFilterCount ||
            query.Filters.Any(f => f.Value.Length > QueryableCollectionLimits.MaxFilterLength))
            throw new ArgumentException("Query exceeds the collection protocol bounds.", nameof(query));
        if ((query.SortColumn is { } sort && !Columns.Any(c => c.Key == sort)) || query.Filters.Keys.Any(key => !Columns.Any(c => c.Key == key)))
            throw new ArgumentException("Unknown query column.", nameof(query));
    }

    private sealed record Collection(QueryableCollection Metadata, SaveNode Array, Func<SaveNode, ViewNode> Details);
    private sealed record Row(SaveNode Node, string Key, string Identity, string Type, string Values)
    {
        public string Search => $"{Identity} {Type} {Values}";
        public string Cell(string column) => column switch
        {
            "identity" => Identity, "type" => Type, "values" => Values, "details" => "", _ => throw new ArgumentException("Unknown column."),
        };
    }
}
