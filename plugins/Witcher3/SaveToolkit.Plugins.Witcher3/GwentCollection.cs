using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>One complete saved card collection; zero copies retains a meaningful native card identity.</summary>
internal sealed class GwentCollection(IReadOnlyList<GwentCard> cards, IReferenceCatalog? catalog, Func<GwentCard, TableRow> row)
{
    public const string CollectionId = "saved/gwent";
    private static IReadOnlyList<QueryableColumn> Columns => [new("image", "Card art"), new("index", "Native card index"),
        new("name", "Reference name"), new("copies", "Saved copies"), new("kind", "Collection group")];
    public QueryableCollection Collection => new(CollectionId, "Gwent collection (native indices)", Columns, cards.Count);
    public QueryableTableNode Build() => new(CollectionId, Collection.Title, Columns, cards.Count);

    public QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        if (query.Cursor is not null || query.Offset < 0 || query.Text?.Length > QueryableCollectionLimits.MaxQueryLength ||
            query.Filters.Count > QueryableCollectionLimits.MaxFilterCount || query.Filters.Any(f =>
                f.Key is not ("image" or "index" or "name" or "copies" or "kind") || f.Value.Length > QueryableCollectionLimits.MaxFilterLength) ||
            query.SortColumn is not (null or "image" or "index" or "name" or "copies" or "kind"))
        {
            throw new ArgumentException("Unsupported Gwent collection query.");
        }

        var matches = cards.Where(c => (string.IsNullOrWhiteSpace(query.Text) ||
            $"{c.Index} {Cell(c, "name")} {c.Copies} {Cell(c, "kind")}".Contains(query.Text, StringComparison.OrdinalIgnoreCase)) &&
            query.Filters.All(f => string.IsNullOrEmpty(f.Value) || string.Equals(Cell(c, f.Key), f.Value, StringComparison.OrdinalIgnoreCase)));
        var sorted = query.SortColumn switch {
            "copies" => Order(matches, c => c.Copies, query.Descending),
            "name" or "image" or "kind" => Order(matches, c => Cell(c, query.SortColumn), query.Descending, StringComparer.OrdinalIgnoreCase),
            _ => Order(matches, c => c.Index, query.Descending),
        };
        var ordered = sorted.ToArray();
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        return new(CollectionId, Columns, ordered.Skip(query.Offset).Take(limit).Select(row).ToArray(),
            cards.Count, ordered.Length, query.Offset, limit);
    }

    private string Cell(GwentCard card, string? column) => column switch {
        "image" => catalog?.Find(Key(card))?.AssetId ?? "",
        "name" => catalog?.Find(Key(card))?.Name ?? Key(card),
        "copies" => card.Copies.ToString(CultureInfo.InvariantCulture),
        "kind" => card.Leader ? "Leader collection" : "Ordinary collection",
        _ => Key(card),
    };
    public static string Key(GwentCard card) => card.Index.ToString(CultureInfo.InvariantCulture);
    private static IOrderedEnumerable<GwentCard> Order<T>(IEnumerable<GwentCard> source, Func<GwentCard, T> key,
        bool descending, IComparer<T>? comparer = null) =>
        (descending ? source.OrderByDescending(key, comparer) : source.OrderBy(key, comparer)).ThenBy(c => c.Index);
}
