using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>One bounded result index per document; paging never retains rendered fields or caller-owned filters.</summary>
internal sealed class CollectionQueryCache<T>
{
    private QueryableCollectionQuery? _criteria;
    private T[]? _rows;

    public T[] Get(QueryableCollectionQuery query, Func<T[]> select)
    {
        if (_criteria is not null && SameCriteria(_criteria, query)) return _rows!;
        var rows = select();
        _criteria = query with { Filters = new Dictionary<string, string>(query.Filters, StringComparer.Ordinal) };
        _rows = rows;
        return rows;
    }

    private static bool SameCriteria(QueryableCollectionQuery cached, QueryableCollectionQuery requested)
    {
        if (cached.Text != requested.Text || cached.SortColumn != requested.SortColumn ||
            cached.Descending != requested.Descending || cached.Filters.Count != requested.Filters.Count)
            return false;
        foreach (var (key, value) in requested.Filters)
        {
            if (!cached.Filters.TryGetValue(key, out var previous) || previous != value) return false;
        }
        return true;
    }
}
