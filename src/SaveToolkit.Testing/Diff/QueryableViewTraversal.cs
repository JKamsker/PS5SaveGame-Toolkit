using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Testing.Diff;

/// <summary>Global F8 budgets and deterministic paging for one plugin document.</summary>
internal sealed class QueryableViewTraversal(ISaveDocument document, SemanticTreeBuilder builder)
{
    private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
    private int _rows;
    private int _pages;
    private long _bytes;

    public bool Visit(QueryableTableNode table, string label, int depth, Func<ViewNode, string, int, string?, bool> visit)
    {
        if (!_visited.Add(table.CollectionId)) return true;
        if (table.TotalCount == 0) return true;
        if (document is not IQueryableDocument queryable)
        {
            builder.MarkTruncated($"Collection '{table.CollectionId}' has no IQueryableDocument provider.");
            return true;
        }

        var limits = builder.Limits;
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxQueryRows);
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxQueryPages);
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxQueryBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(limits.QueryPageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limits.QueryPageSize, QueryableCollectionLimits.MaxPageSize);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var offset = 0;
        string? cursor = null;
        while (true)
        {
            if (_rows >= limits.MaxQueryRows) return Cut(table, $"row cap {limits.MaxQueryRows}");
            if (_pages >= limits.MaxQueryPages) return Cut(table, $"page cap {limits.MaxQueryPages}");
            if (_bytes >= limits.MaxQueryBytes) return Cut(table, $"byte cap {limits.MaxQueryBytes}");

            var size = Math.Min(limits.QueryPageSize, limits.MaxQueryRows - _rows);
            var query = new QueryableCollectionQuery { Offset = cursor is null ? offset : 0, Cursor = cursor, Limit = size };
            var page = queryable.Query(table.CollectionId, query);
            _pages++;
            if (page.CollectionId != table.CollectionId || page.Rows.Count > size || page.Limit != size || page.Offset < 0 ||
                cursor is null && page.Offset != offset || page.TotalCount != table.TotalCount ||
                page.FilteredCount != page.TotalCount || page.TotalCount < 0)
            {
                return Cut(table, "invalid page metadata");
            }

            // Count the complete UTF-8 page, including read-only cells/metadata, without keeping a JSON copy.
            using var counter = new QueryPageByteCounter(limits.MaxQueryBytes - _bytes);
            try
            {
                JsonSerializer.Serialize(counter, page);
            }
            catch (QueryPageByteLimitException)
            {
                _bytes = limits.MaxQueryBytes;
                return Cut(table, $"byte cap {limits.MaxQueryBytes}");
            }

            _bytes += counter.BytesWritten;
            foreach (var row in page.Rows.OrderBy(r => r.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(row.Key) || !keys.Add(row.Key)) return Cut(table, "duplicate or empty row identity");
                _rows++;
                foreach (var cell in row.Cells.OrderBy(c => c.Key, StringComparer.Ordinal))
                {
                    var path = $"collections/{Escape(table.CollectionId)}/rows/{Escape(row.Key)}/cells/{Escape(cell.Key)}";
                    if (!visit(cell.Value, PluginViewTraversal.Join(label, row.Key), depth + 1, path)) return false;
                }
            }

            offset = checked(offset + page.Rows.Count);
            if (page.Rows.Count == 0)
            {
                if (page.NextCursor is not null || offset < page.FilteredCount) return Cut(table, "empty page before the collection ended");
                return true;
            }

            if (page.NextCursor is { } next)
            {
                if (next.Length > QueryableCollectionLimits.MaxCursorLength || !cursors.Add(next)) return Cut(table, "invalid or repeated cursor");
                cursor = next;
            }
            else
            {
                if (offset >= page.FilteredCount) return true;
                if (cursor is not null) return Cut(table, "cursor ended before the collection did");
            }
        }
    }

    private bool Cut(QueryableTableNode table, string reason)
    {
        builder.MarkTruncated($"Collection '{table.CollectionId}' traversal stopped: {reason}.");
        return true;
    }

    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
