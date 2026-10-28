using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Sessions;

/// <summary>Locked access to optional server-backed collections.</summary>
public sealed partial class DocumentSession
{
    public IReadOnlyList<QueryableCollection> QueryableCollections => Locked(() =>
        Document is IQueryableDocument queryable ? queryable.Collections : []);

    /// <summary>
    /// Runs one bounded collection query and registers the returned row bindings. A revision supplied by
    /// the browser must still be current; this prevents a page from an old structure being edited.
    /// </summary>
    public QueryableCollectionPage QueryCollection(string collectionId, QueryableCollectionQuery query, long? expectedRevision = null) => Locked(() =>
    {
        if (Document is not IQueryableDocument queryable)
        {
            throw new KeyNotFoundException("This document has no queryable collections.");
        }

        // A page is stale only across structural changes; value edits keep row identities valid.
        if (expectedRevision is { } expected && (expected < _structuralRevision || expected > _revision))
        {
            throw new StaleViewException();
        }

        ValidateQuery(collectionId, query);
        var collection = queryable.Collections.FirstOrDefault(c => string.Equals(c.Id, collectionId, StringComparison.Ordinal));
        if (collection is null)
        {
            throw new KeyNotFoundException($"Unknown collection '{collectionId}'.");
        }

        var columns = collection.Columns.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        if (query.SortColumn is not null && !columns.Contains(query.SortColumn))
        {
            throw new ArgumentException($"Unknown sort column '{query.SortColumn}'.", nameof(query));
        }

        if (query.Filters.Keys.Any(key => !columns.Contains(key)))
        {
            throw new ArgumentException("A collection filter names an unknown column.", nameof(query));
        }

        var page = queryable.Query(collectionId, query);
        if (page is null)
        {
            throw new InvalidOperationException($"Collection '{collection.Id}' returned no page.");
        }
        ValidatePage(collection, page, query);
        _index.AddAll(page.Rows.SelectMany(r => r.Cells.Values));
        return page with
        {
            CollectionId = collection.Id,
            Columns = collection.Columns,
            TotalCount = collection.TotalCount,
            Revision = _revision,
        };
    });

    private static void ValidateQuery(string collectionId, QueryableCollectionQuery query)
    {
        if (string.IsNullOrWhiteSpace(collectionId))
        {
            throw new ArgumentException("Collection id is required.", nameof(collectionId));
        }

        ArgumentNullException.ThrowIfNull(query);

        if (query.Limit is < 1 or > QueryableCollectionLimits.MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Limit, $"Page size must be between 1 and {QueryableCollectionLimits.MaxPageSize}.");
        }

        if (query.Offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Offset, "Offset cannot be negative.");
        }

        if (query.Cursor is not null && (query.Cursor.Length == 0 || query.Cursor.Length > QueryableCollectionLimits.MaxCursorLength || query.Offset != 0))
        {
            throw new ArgumentException("Cursor must be non-empty, bounded and used without an offset.", nameof(query));
        }

        if (query.Text is { Length: > QueryableCollectionLimits.MaxQueryLength })
        {
            throw new ArgumentException($"Search text cannot exceed {QueryableCollectionLimits.MaxQueryLength} characters.", nameof(query));
        }

        if (query.Filters.Count > QueryableCollectionLimits.MaxFilterCount || query.Filters.Any(f => f.Key.Length == 0 || f.Key.Length > 128 || f.Value.Length > QueryableCollectionLimits.MaxFilterLength))
        {
            throw new ArgumentException("Collection filters exceed the protocol limits.", nameof(query));
        }
    }

    private static void ValidatePage(QueryableCollection collection, QueryableCollectionPage page, QueryableCollectionQuery query)
    {
        if (!string.Equals(page.CollectionId, collection.Id, StringComparison.Ordinal)
            || page.Rows.Count > query.Limit
            || page.Limit != query.Limit
            || query.Cursor is null && page.Offset != query.Offset
            || page.TotalCount != collection.TotalCount
            || page.TotalCount < 0
            || page.FilteredCount < 0
            || page.FilteredCount > page.TotalCount
            || page.Offset < 0)
        {
            throw new InvalidOperationException($"Collection '{collection.Id}' returned an invalid page.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in page.Rows)
        {
            if (string.IsNullOrWhiteSpace(row.Key) || !keys.Add(row.Key))
            {
                throw new InvalidOperationException($"Collection '{collection.Id}' returned duplicate or empty row identities.");
            }
        }
    }
}
