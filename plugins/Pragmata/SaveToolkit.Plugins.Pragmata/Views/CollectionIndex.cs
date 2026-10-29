using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.Pragmata.Views;

/// <summary>Lazy model projections and one ordered match set; never retains page views or bindings.</summary>
internal sealed class CollectionIndex<T>
{
    private T[]? _rows;
    private T[]? _matches;
    private QueryableCollectionQuery? _criteria;

    public T[] Match(QueryableCollectionQuery query, Func<T[]> describe,
        Func<IEnumerable<T>, QueryableCollectionQuery, IEnumerable<T>> select)
    {
        _rows ??= describe();
        if (_criteria is not null && SameCriteria(_criteria, query)) return _matches!;
        var matches = select(_rows, query).ToArray();
        // Filters are caller-owned and may be mutated between requests. Keep only one snapshot,
        // excluding offset/limit so all pages of the same search share its filter/sort work.
        _criteria = query with { Filters = new Dictionary<string, string>(query.Filters, StringComparer.Ordinal) };
        _matches = matches;
        return matches;
    }

    public void Invalidate()
    {
        _rows = null;
        _matches = null;
        _criteria = null;
    }

    private static bool SameCriteria(QueryableCollectionQuery left, QueryableCollectionQuery right) =>
        left.Text == right.Text && left.SortColumn == right.SortColumn && left.Descending == right.Descending &&
        left.Filters.Count == right.Filters.Count && left.Filters.All(filter =>
            right.Filters.TryGetValue(filter.Key, out var value) && filter.Value == value);
}
