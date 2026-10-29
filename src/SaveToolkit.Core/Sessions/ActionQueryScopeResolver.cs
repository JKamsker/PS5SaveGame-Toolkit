using System.Collections.ObjectModel;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Core.Sessions;

/// <summary>Materializes a bounded whole-query selection using the session's validated F8 query path.</summary>
internal static class ActionQueryScopeResolver
{
    public static ActionScope Resolve(ActionQueryScope scope, int maxRows,
        Func<QueryableCollectionQuery, QueryableCollectionPage> queryPage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(scope.Query);
        ArgumentNullException.ThrowIfNull(scope.ExcludedRowKeys);
        if (scope.ExcludedRowKeys.Count > maxRows
            || scope.ExcludedRowKeys.Any(string.IsNullOrEmpty)
            || scope.ExcludedRowKeys.Distinct(StringComparer.Ordinal).Count() != scope.ExcludedRowKeys.Count)
        {
            throw new ArgumentException($"Query exclusions must contain at most {maxRows} distinct row keys.");
        }

        // Paging is transport state, never part of a whole-query selection. Retain no caller-owned collections.
        var query = scope.Query with
        {
            Filters = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(scope.Query.Filters, StringComparer.Ordinal)),
            Offset = 0,
            Cursor = null,
            Limit = Math.Min(maxRows, QueryableCollectionLimits.MaxPageSize),
        };
        var normalized = scope with { Query = query, ExcludedRowKeys = Array.AsReadOnly(scope.ExcludedRowKeys.ToArray()) };
        var excluded = normalized.ExcludedRowKeys.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var selected = new List<string>();
        long? filteredCount = null;
        var pageQuery = query;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = queryPage(pageQuery);
            cancellationToken.ThrowIfCancellationRequested();
            if (page.FilteredCount > maxRows)
            {
                throw new ArgumentException($"Query selection exceeds the limit of {maxRows} matching rows; narrow the query.");
            }

            if (filteredCount is { } count && page.FilteredCount != count)
            {
                throw new InvalidOperationException("Collection returned inconsistent query-selection pages.");
            }

            filteredCount = page.FilteredCount;
            foreach (var row in page.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(row.Key))
                {
                    throw new InvalidOperationException("Collection repeated a row across query-selection pages.");
                }

                if (!excluded.Contains(row.Key)) selected.Add(row.Key);
            }

            if (seen.Count > filteredCount || seen.Count == filteredCount && page.NextCursor is not null
                || seen.Count < filteredCount && page.Rows.Count == 0)
            {
                throw new InvalidOperationException("Collection returned incomplete or invalid query-selection pages.");
            }

            if (seen.Count == filteredCount) break;
            pageQuery = query with { Offset = page.NextCursor is null ? seen.Count : 0, Cursor = page.NextCursor };
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new ActionScope(Array.AsReadOnly(selected.ToArray())) { QueryScope = normalized };
    }
}
