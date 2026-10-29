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
            Description = $"Identifier names: {HashNames.Attribution} Restored identifiers: {HashNames.RestoredAttribution} " +
                "Counts are stored records, not completion totals. " +
                $"Search covers raw IDs, types and the first {MaximumSummaryFields} direct stored fields. Gameplay meanings are Unknown. " +
                "Row keys identify fixed plaintext offsets; they do not establish a gameplay identity.",
        };
        if (_collections.TryGetValue(id, out var existing) && ReferenceEquals(existing.Array, array))
            existing.Details = details;
        else
            _collections[id] = new Collection(metadata, array, details);
        return new QueryableTableNode(id, metadata.Title, Columns, metadata.TotalCount) { Description = metadata.Description };
    }

    public QueryableCollectionPage Query(string id, QueryableCollectionQuery query)
    {
        if (!_collections.TryGetValue(id, out var collection)) throw new KeyNotFoundException($"Unknown Pragmata collection '{id}'.");
        CollectionQueries.Validate(query, Columns);
        var filtered = collection.Index.Match(query, () => collection.Array.Children.Select(Describe).ToArray(), Select);
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

    public void Invalidate()
    {
        foreach (var collection in _collections.Values) collection.Index.Invalidate();
    }

    private static IEnumerable<Row> Select(IEnumerable<Row> rows, QueryableCollectionQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Text))
            rows = rows.Where(row => row.Search.Contains(query.Text, StringComparison.OrdinalIgnoreCase));
        foreach (var (column, value) in query.Filters)
            rows = rows.Where(row => string.Equals(row.Cell(column), value, StringComparison.OrdinalIgnoreCase));
        var sort = query.SortColumn ?? "identity";
        var ordered = query.Descending
            ? rows.OrderByDescending(row => row.Cell(sort), StringComparer.Ordinal)
            : rows.OrderBy(row => row.Cell(sort), StringComparer.Ordinal);
        return ordered.ThenBy(row => row.Key, StringComparer.Ordinal);
    }

    private Row Describe(SaveNode node)
    {
        var fields = node.Type == FieldTags.Class ? node.Children : [node];
        var identity = fields.FirstOrDefault(field => HashNames.All.GetValueOrDefault(field.Hash) is
            "_ID" or "_Id" or "ID" or "Id" or "ChapterIDHash" or "_EventType" or "_RecordType");
        var label = identity is null ? $"plaintext @0x{node.Offset:x}" : $"{HashNames.Label(identity.Hash)}={Value(identity)}";
        return new Row(node, $"record-at-{node.Offset:x}", label,
            node.Type == FieldTags.Class ? HashNames.Label(node.ClassHash) : FieldTags.ShortName(node.Type), this);
    }

    private string Summary(SaveNode node)
    {
        var fields = node.Type == FieldTags.Class ? node.Children : [node];
        var values = string.Join("; ", fields.Take(MaximumSummaryFields).Select(field =>
            $"{HashNames.Label(field.Hash)}={Value(field)}"));
        return fields.Count > MaximumSummaryFields ? values + "; … (open details for remaining fields)" : values;
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

    private sealed class Collection(QueryableCollection metadata, SaveNode array, Func<SaveNode, ViewNode> details)
    {
        public QueryableCollection Metadata { get; } = metadata;
        public SaveNode Array { get; } = array;
        public Func<SaveNode, ViewNode> Details { get; set; } = details;
        public CollectionIndex<Row> Index { get; } = new();
    }

    private sealed record Row(SaveNode Node, string Key, string Identity, string Type, EvidenceCollections Owner)
    {
        private string? _values;
        private string? _search;
        public string Values => _values ??= Owner.Summary(Node);
        public string Search => _search ??= $"{Identity} {Type} {Values}";
        public string Cell(string column) => column switch
        {
            "identity" => Identity, "type" => Type, "values" => Values, "details" => "", _ => throw new ArgumentException("Unknown column."),
        };
    }
}
