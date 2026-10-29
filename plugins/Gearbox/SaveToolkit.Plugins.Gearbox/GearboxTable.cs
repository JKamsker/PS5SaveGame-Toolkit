using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>One table per stored list; large lists bind only the requested query page.</summary>
internal sealed class GearboxTable(string id, string title, IReadOnlyList<QueryableColumn> columns,
    Func<IEnumerable<GearboxQueryRow>> rows, EmptyRowPresentation? emptyRows = null)
{
    private const int EagerRowLimit = 100;
    public QueryableCollection Collection => new(id, title, columns, rows().Count()) { EmptyRows = emptyRows };

    public ViewNode Build()
    {
        var collection = Collection;
        if (collection.TotalCount > EagerRowLimit)
            return new QueryableTableNode(id, title, columns, collection.TotalCount) { EmptyRows = emptyRows };
        return new TableNode(columns.Select(c => new TableColumn(c.Key, c.Label) { Width = c.Width }).ToArray(),
            rows().Select(row => row.BuildRow()).ToArray()) { EmptyRows = emptyRows };
    }

    public QueryableCollectionPage Query(QueryableCollectionQuery query) => GearboxQuery.Page(Collection, query, rows(), preserveStoredOrder: true);
}
