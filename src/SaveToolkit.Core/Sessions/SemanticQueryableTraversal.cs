using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Sessions;

/// <summary>One snapshot's global F8 budgets. Providers own deterministic native row ordering.</summary>
internal sealed class SemanticQueryableTraversal(ISaveDocument document, DocumentSessionOptions limits)
{
    private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = [];
    private int _rows;
    private int _pages;
    private long _bytes;
    public IReadOnlyList<string> Warnings => _warnings;

    public void MarkTruncated(QueryableTableNode table, string reason) => Cut(table, reason);

    public void Visit(QueryableTableNode table, Func<TableRow, SemanticCollectionTarget, string, bool> visit)
    {
        if (!_visited.Add(table.CollectionId)) return;
        if (table.TotalCount == 0) return;
        if (table.TotalCount < 0 || document is not IQueryableDocument)
        {
            Cut(table, "invalid total or missing IQueryableDocument provider");
            return;
        }

        try { Read(table, (IQueryableDocument)document, visit); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Cut(table, "collection could not be read");
        }
    }

    private void Read(QueryableTableNode table, IQueryableDocument queryable,
        Func<TableRow, SemanticCollectionTarget, string, bool> visit)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var offset = 0;
        string? cursor = null;
        while (true)
        {
            if (_rows >= limits.MaxReviewQueryRows) { Cut(table, $"row cap {limits.MaxReviewQueryRows}"); return; }
            if (_pages >= limits.MaxReviewQueryPages) { Cut(table, $"page cap {limits.MaxReviewQueryPages}"); return; }
            if (_bytes >= limits.MaxReviewQueryBytes) { Cut(table, $"byte cap {limits.MaxReviewQueryBytes}"); return; }
            var size = Math.Min(QueryableCollectionLimits.MaxPageSize, limits.MaxReviewQueryRows - _rows);
            var query = new QueryableCollectionQuery { Offset = cursor is null ? offset : 0, Cursor = cursor, Limit = size, IncludeEmpty = true };
            _pages++;
            var page = queryable.Query(table.CollectionId, query);
            if (page.CollectionId != table.CollectionId || page.Rows.Count > size || page.Limit != size || page.Offset < 0 ||
                cursor is null && page.Offset != offset || page.TotalCount != table.TotalCount ||
                page.FilteredCount != page.TotalCount || page.HiddenEmptyCount != 0 || page.Rows.Count > page.TotalCount - offset)
            {
                Cut(table, "invalid page metadata or rows beyond the declared total");
                return;
            }

            using var counter = new SemanticQueryByteCounter(limits.MaxReviewQueryBytes - _bytes);
            try { JsonSerializer.Serialize(counter, page); }
            catch (SemanticQueryByteLimitException)
            {
                _bytes = limits.MaxReviewQueryBytes;
                Cut(table, $"byte cap {limits.MaxReviewQueryBytes}");
                return;
            }

            _bytes += counter.BytesWritten;
            foreach (var row in page.Rows)
            {
                if (string.IsNullOrWhiteSpace(row.Key) || !keys.Add(row.Key))
                {
                    Cut(table, "duplicate or empty row identity");
                    return;
                }

                _rows++;
                var target = new SemanticCollectionTarget(table.CollectionId, row.Key, query);
                if (!visit(row, target, $"{table.Title} / {row.Key}"))
                {
                    Cut(table, "view memory or traversal cap");
                    return;
                }
            }

            offset = checked(offset + page.Rows.Count);
            // A trailing cursor cannot introduce rows beyond the provider's declared total.
            if (offset == page.TotalCount) return;
            if (page.Rows.Count == 0) { Cut(table, "empty page before the collection ended"); return; }
            if (page.NextCursor is { } next)
            {
                if (next.Length == 0 || next.Length > QueryableCollectionLimits.MaxCursorLength || !cursors.Add(next))
                {
                    Cut(table, "invalid or repeated cursor");
                    return;
                }

                cursor = next;
            }
            else if (cursor is not null) { Cut(table, "cursor ended before the collection did"); return; }
        }
    }

    private void Cut(QueryableTableNode table, string reason) =>
        _warnings.Add($"Semantic review truncated for collection '{table.CollectionId}': {reason}. Only proven changes are listed.");
}
