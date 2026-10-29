using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>Query-only bindings, hidden empty slots and optional cursor/malformed pages.</summary>
internal sealed class SemanticQueryableDocument(int rowCount = 251, int collectionCount = 1) : BoundSaveDocument, IQueryableDocument
{
    private static readonly QueryableColumn[] Columns = [new("name", "Name"), new("quantity", "Quantity", IntegerSpec.Int32)];
    private readonly Dictionary<string, long> _values = new(StringComparer.Ordinal);
    public List<(string Collection, QueryableCollectionQuery Query)> Queries { get; } = [];
    public Func<QueryableCollectionPage, QueryableCollectionPage>? TransformPage { get; init; }
    public bool CursorPaging { get; init; }
    public bool DuplicateTables { get; init; }
    public string Padding { get; init; } = "";
    public override string Title => "Semantic inventory";
    public IReadOnlyList<QueryableCollection> Collections => Enumerable.Range(0, collectionCount)
        .Select(i => new QueryableCollection(Collection(i), "Inventory", Columns, rowCount)
        { EmptyRows = new("name") }).ToArray();

    public static string Collection(int index) => $"inventory/{index}";
    public static string Row(int index) => $"slot/{index}";
    public static string ValueId(int collection, int row) => $"{Collection(collection)}/{Row(row)}/quantity";
    public void SetQuantity(int collection, int row, long value) => _values[ValueId(collection, row)] = value;

    public override ViewNode BuildView() => new SectionNode("Root", [SectionNode.Lazy("Inventory", "inventory")]);
    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        var tables = Collections.Select(c => (ViewNode)new QueryableTableNode(c.Id, c.Title, c.Columns, c.TotalCount)
        { EmptyRows = c.EmptyRows }).ToArray();
        return DuplicateTables ? [.. tables, .. tables] : tables;
    }

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        Queries.Add((collectionId, query));
        var offset = query.Cursor is { } cursor ? int.Parse(cursor, CultureInfo.InvariantCulture) : query.Offset;
        var rows = Enumerable.Range(0, rowCount).Where(i => query.IncludeEmpty == true || i != 0)
            .Skip(offset).Take(query.Limit).Select(i => new TableRow(Row(i), new Dictionary<string, ViewNode>
            {
                ["name"] = new TextNode($"Item {i}" + Padding),
                ["quantity"] = Quantity(collectionId, i),
            }) { IsEmpty = i == 0 }).ToArray();
        var page = new QueryableCollectionPage(collectionId, Columns, rows, rowCount,
            rowCount - (query.IncludeEmpty == true ? 0 : 1), offset, query.Limit)
        { NextCursor = CursorPaging && offset + rows.Length < rowCount ? (offset + rows.Length).ToString(CultureInfo.InvariantCulture) : null };
        return TransformPage?.Invoke(page) ?? page;
    }

    private FieldNode Quantity(string collectionId, int row)
    {
        var id = $"{collectionId}/{Row(row)}/quantity";
        return Field(id, "Quantity", IntegerSpec.Int32,
            () => FieldValue.Of(_values.GetValueOrDefault(id, row)), value => _values[id] = value.AsInt64());
    }

    public override IReadOnlyList<FileOutput> Serialize() => [];
}
