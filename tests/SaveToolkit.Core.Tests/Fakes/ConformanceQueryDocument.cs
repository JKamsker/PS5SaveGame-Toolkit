using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>Small F8-only view; mutations let conformance tests prove that malformed pages are rejected.</summary>
internal sealed class ConformanceQueryDocument(bool cursor = false, bool lazy = false, int count = 102)
    : BoundSaveDocument, IQueryableDocument
{
    private static readonly IReadOnlyList<QueryableColumn> Columns = [new("value", "Value", IntegerSpec.Int32)];
    private readonly Dictionary<int, long> _values = Enumerable.Range(0, count).ToDictionary(i => i, i => (long)i);

    public List<QueryableCollectionQuery> Queries { get; } = [];
    public List<string> QueriedCollections { get; } = [];
    public Func<QueryableCollectionPage, QueryableCollectionPage>? TransformPage { get; init; }
    public bool DuplicateTable { get; init; }
    public bool SecondCollection { get; init; }
    public override string Title => "F8 conformance";
    public IReadOnlyList<QueryableCollection> Collections => SecondCollection
        ? [new("items", Title, Columns, count), new("other", Title, Columns, count)]
        : [new("items", Title, Columns, count)];

    public override ViewNode BuildView() => new SectionNode(Title,
        lazy ? [SectionNode.Lazy("Inventory", "inventory")] : Tables());

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) =>
        lazyId == "inventory" ? Tables() : throw new KeyNotFoundException(lazyId);

    private IReadOnlyList<ViewNode> Tables()
    {
        var table = new QueryableTableNode("items", Title, Columns, count);
        if (DuplicateTable) return [table, table];
        if (SecondCollection) return [table, table with { CollectionId = "other" }];
        return [table];
    }

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        Queries.Add(query);
        QueriedCollections.Add(collectionId);
        var offset = query.Cursor is null ? query.Offset : int.Parse(query.Cursor, CultureInfo.InvariantCulture);
        var rows = Enumerable.Range(offset, Math.Min(query.Limit, count - offset)).Select(i =>
            new TableRow(i.ToString(CultureInfo.InvariantCulture), new Dictionary<string, ViewNode>
            {
                ["value"] = Field($"item/{i}", $"Item {i}", IntegerSpec.Int32,
                    () => FieldValue.Of(_values[i]), value => _values[i] = value.AsInt32()),
            }) { IsEmpty = i == 0 }).ToArray();
        var page = new QueryableCollectionPage(collectionId, Columns, rows, count, count, offset, query.Limit)
        {
            NextCursor = cursor && offset + rows.Length < count
                ? (offset + rows.Length).ToString(CultureInfo.InvariantCulture) : null,
        };
        return TransformPage?.Invoke(page) ?? page;
    }

    public override IReadOnlyList<FileOutput> Serialize() => [];
}
