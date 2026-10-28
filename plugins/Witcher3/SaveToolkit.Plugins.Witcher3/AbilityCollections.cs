using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed class AbilityCollections(IReadOnlyList<AbilityArray> arrays, IReferenceCatalog? catalog,
    SkillRanks? edits, Func<AbilityArray, AbilityEntry, TableRow> row)
{
    public const string Prefix = "saved/abilities/";
    public static IReadOnlyList<QueryableColumn> Columns => [new("id", "Saved identity"), new("name", "Reference name"),
        new("rank", "Stored rank"), new("properties", "Input properties (read-only)")];
    public IReadOnlyList<QueryableCollection> Collections => arrays.Select(a => new QueryableCollection(
        Prefix + a.Name, a.Name, Columns, a.Entries.Count)).ToArray();

    public IReadOnlyList<ViewNode> Build() => Collections.Select(c => (ViewNode)new QueryableTableNode(c.Id, c.Title, c.Columns, c.TotalCount)).ToArray();

    public QueryableCollectionPage Query(string id, QueryableCollectionQuery query)
    {
        var array = arrays.SingleOrDefault(a => Prefix + a.Name == id) ?? throw new KeyNotFoundException(id);
        if (query.Cursor is not null || query.Offset < 0 || query.Text?.Length > QueryableCollectionLimits.MaxQueryLength ||
            query.Filters.Count > QueryableCollectionLimits.MaxFilterCount || query.Filters.Any(f => f.Key is not ("id" or "name" or "rank") ||
                f.Value.Length > QueryableCollectionLimits.MaxFilterLength) || query.SortColumn is not (null or "id" or "name" or "rank" or "properties"))
        {
            throw new ArgumentException("Unsupported ability query.");
        }
        string Cell(AbilityEntry e, string? column) => column switch {
            "name" => catalog?.Find(e.Id)?.Name ?? e.Id,
            "rank" => Rank(e)?.ToString(CultureInfo.InvariantCulture) ?? "Unknown / definition", _ => e.Id,
        };
        int? Rank(AbilityEntry e)
        {
            if (array.Name != "skills") { return null; }
            try { return edits?.Find(e.Id)?.Value ?? e.Record.Integer("level"); }
            catch (SaveToolkit.Sdk.SaveFormatException) { return null; }
        }
        var matches = array.Entries.Where(e => (string.IsNullOrWhiteSpace(query.Text) ||
            $"{e.Id} {Cell(e, "name")} {Cell(e, "rank")}".Contains(query.Text, StringComparison.OrdinalIgnoreCase)) &&
            query.Filters.All(f => string.IsNullOrEmpty(f.Value) || string.Equals(Cell(e, f.Key), f.Value, StringComparison.OrdinalIgnoreCase)));
        IOrderedEnumerable<AbilityEntry> sorted = query.SortColumn == "rank"
            ? query.Descending ? matches.OrderByDescending(Rank) : matches.OrderBy(Rank)
            : query.Descending ? matches.OrderByDescending(e => Cell(e, query.SortColumn), StringComparer.OrdinalIgnoreCase)
                : matches.OrderBy(e => Cell(e, query.SortColumn), StringComparer.OrdinalIgnoreCase);
        var ordered = sorted.ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        return new(id, Columns, ordered.Skip(query.Offset).Take(limit).Select(e => row(array, e)).ToArray(),
            array.Entries.Count, ordered.Length, query.Offset, limit);
    }
}
