using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using Xunit;

namespace SaveToolkit.Testing;

/// <summary>Samples query pages with one budget per view walk and validates paging progress and identities.</summary>
internal sealed class ConformanceQueryPages(ISaveDocument document, int pageBudget)
{
    private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
    private int _remaining = pageBudget;

    public IEnumerable<TableNode> Read(QueryableTableNode table)
    {
        if (!_visited.Add(table.CollectionId)) yield break;
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var offset = 0;
        string? cursor = null;
        while (_remaining > 0)
        {
            _remaining--;
            var query = new QueryableCollectionQuery
            {
                Offset = cursor is null ? offset : 0,
                Cursor = cursor,
                Limit = QueryableCollectionLimits.DefaultPageSize,
                IncludeEmpty = true,
            };
            var page = queryable.Query(table.CollectionId, query);
            Assert.Equal(table.CollectionId, page.CollectionId);
            Assert.Equal(table.TotalCount, page.TotalCount);
            Assert.True(page.TotalCount >= 0);
            Assert.Equal(page.TotalCount, page.FilteredCount);
            Assert.Equal(0, page.HiddenEmptyCount);
            Assert.Equal(query.Limit, page.Limit);
            Assert.InRange(page.Rows.Count, 0, query.Limit);
            Assert.Equal(offset, page.Offset);
            Assert.True(offset + (long)page.Rows.Count <= page.FilteredCount, "Page exceeds the collection size.");

            var columns = page.Columns.Select(column => column.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var row in page.Rows)
            {
                Assert.False(string.IsNullOrWhiteSpace(row.Key), "Collection returned an empty row identity.");
                Assert.True(keys.Add(row.Key), $"Collection '{table.CollectionId}' repeated row '{row.Key}'.");
                Assert.All(row.Cells.Keys, key => Assert.Contains(key, columns));
            }

            yield return new TableNode(page.Columns.Select(column => new TableColumn(column.Key, column.Label)).ToList(), page.Rows);

            offset = checked(offset + page.Rows.Count);
            if (page.NextCursor is { } next)
            {
                Assert.InRange(next.Length, 1, QueryableCollectionLimits.MaxCursorLength);
                Assert.True(cursors.Add(next), $"Collection '{table.CollectionId}' repeated a cursor.");
                Assert.NotEmpty(page.Rows);
                cursor = next;
            }
            else
            {
                if (offset >= page.FilteredCount) yield break;
                Assert.Null(cursor);
                Assert.NotEmpty(page.Rows);
            }
        }
    }
}
