using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.SaveDiff.Tests;

/// <summary>Synthetic identity-sorted inventory, with selectable offset/cursor and malformed-page cases.</summary>
internal sealed class QueryableInventoryPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("test.queryable", "Queryable inventory", "1.0.0");
    public bool UseCursor { get; init; }
    public bool Lazy { get; init; }
    public int CollectionCount { get; init; } = 1;
    public bool DuplicateTables { get; init; }
    public bool DuplicateField { get; init; }
    public bool Disposed { get; private set; }
    public List<QueryableCollectionQuery> Queries { get; } = [];
    public Func<QueryableCollectionPage, QueryableCollectionPage>? TransformPage { get; init; }

    public ProbeResult Probe(ISaveFile file) => new InventoryPlugin().Probe(file);

    public ISaveDocument Open(ISaveFile file)
    {
        var lines = System.Text.Encoding.UTF8.GetString(file.ReadAllBytes()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var items = lines.Skip(1).Select(l => l.Split('=')).ToDictionary(p => p[0], p => long.Parse(p[1], CultureInfo.InvariantCulture), StringComparer.Ordinal);
        return new Document(this, items);
    }

    private sealed class Document(QueryableInventoryPlugin owner, IReadOnlyDictionary<string, long> items) : ISaveDocument, IQueryableDocument
    {
        private static readonly QueryableColumn[] Columns = [new("quantity", "Quantity", IntegerSpec.Int32), new("state", "State")];
        public string Title => "Queryable inventory";
        public bool CanEdit => false;

        public IReadOnlyList<QueryableCollection> Collections => [.. Enumerable.Range(0, owner.CollectionCount)
            .Select(i => new QueryableCollection($"inventory{i}", "Inventory", Columns, items.Count))];

        public ViewNode BuildView() => new SectionNode("Root", owner.Lazy ? [SectionNode.Lazy("Lazy", "inventory")] : Tables());

        public IReadOnlyList<ViewNode> ExpandNode(string lazyId) => Tables();

        private IReadOnlyList<ViewNode> Tables()
        {
            var tables = Collections.Select(c => (ViewNode)new QueryableTableNode(c.Id, c.Title, c.Columns, c.TotalCount)).ToList();
            if (owner.DuplicateTables) tables.AddRange(tables.ToArray());
            if (owner.DuplicateField && items.ContainsKey("1")) tables.Insert(0, Quantity("inventory0", "1", items["1"]));
            return tables;
        }

        public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
        {
            owner.Queries.Add(query);
            var offset = query.Cursor is { } cursor ? int.Parse(cursor, CultureInfo.InvariantCulture) : query.Offset;
            var rows = items.OrderBy(i => i.Key, StringComparer.Ordinal).Skip(offset).Take(query.Limit)
                .Select(i => new TableRow($"item/{i.Key}", new Dictionary<string, ViewNode>(StringComparer.Ordinal)
                {
                    ["state"] = new TextNode($"stored:{i.Value.ToString(CultureInfo.InvariantCulture)}"),
                    ["quantity"] = Quantity(collectionId, i.Key, i.Value),
                })).ToArray();
            var page = new QueryableCollectionPage(collectionId, Columns, rows, items.Count, items.Count, offset, query.Limit)
            {
                NextCursor = owner.UseCursor && offset + rows.Length < items.Count
                    ? (offset + rows.Length).ToString(CultureInfo.InvariantCulture) : null,
            };
            return owner.TransformPage?.Invoke(page) ?? page;
        }

        private static FieldNode Quantity(string collection, string id, long value) =>
            new($"{collection}/{id}/quantity", "Quantity", IntegerSpec.Int32, FieldValue.Of(value));

        public FieldValue GetValue(string valueId) => throw new NotSupportedException();
        public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => throw new NotSupportedException();
        public IReadOnlyList<FileOutput> Serialize() => throw new NotSupportedException();
        public void Dispose() => owner.Disposed = true;
    }
}
