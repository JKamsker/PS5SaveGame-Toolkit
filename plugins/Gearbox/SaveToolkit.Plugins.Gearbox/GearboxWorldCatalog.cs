using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Attributed labels, never legality/capacity rules. Unknown assets retain their raw IDs.</summary>
internal sealed class GearboxWorldCatalog(GearboxDocument document, IReferenceContext references)
{
    internal const string CatalogId = "world-labels";
    private static readonly QueryableColumn[] Columns = [new("name", "Name"), new("category", "Category"), new("id", "Raw lookup ID")];
    private SerialDatabase Database => SerialDatabase.For(document.Root.Schema.Wonderlands);
    private IReferenceCatalog? Catalog => references.Catalog(CatalogId);
    public QueryableCollection Collection => new(CatalogId, "World / progression labels", Columns, Database.Records(CatalogId).Count());
    public QueryableTableNode Build() => new(CatalogId, Collection.Title, Columns, Collection.TotalCount)
    { Description = "English upstream labels; game patch/DLC coverage unknown. Travel labels name destination maps, not stations. Myth limits are not validated PS5 edit rules." };

    internal ReferenceRecord? Find(string id) => Catalog?.Find(id) ?? Database.Find(CatalogId, id);

    public FieldNode Label(string id, string label, string path, bool mission = false)
    {
        var key = mission ? path.Split('.')[0].ToLowerInvariant() : path.ToLowerInvariant();
        var record = Find(key);
        return document.InspectionField(id, label, record is null ? path : $"{record.Name} — {path}",
            record is null ? Confidence.Unknown : Confidence.Inferred, record?.AttributionText);
    }

    public QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        if (query.Cursor is not null) throw new ArgumentException("Gearbox labels use offset paging.");
        if (query.SortColumn is { } sort && Columns.All(c => c.Key != sort) || query.Filters.Keys.Any(k => Columns.All(c => c.Key != k)))
            throw new ArgumentException("Unknown label sort/filter column.");
        var entries = Database.Records(CatalogId).Select(r => Find(r.Key)!).Where(r =>
            (string.IsNullOrEmpty(query.Text) || new[] { r.Name, r.Key, r.Category }.Any(v => v?.Contains(query.Text, StringComparison.OrdinalIgnoreCase) == true)) &&
            query.Filters.All(f => string.Equals(Value(r, f.Key), f.Value, StringComparison.OrdinalIgnoreCase))).ToArray();
        var key = query.SortColumn ?? "id";
        var ordered = query.Descending ? entries.OrderByDescending(r => Value(r, key), StringComparer.OrdinalIgnoreCase)
            : entries.OrderBy(r => Value(r, key), StringComparer.OrdinalIgnoreCase);
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        return new(CatalogId, Columns, ordered.ThenBy(r => r.Key, StringComparer.Ordinal).Skip(offset).Take(limit).Select(r => new TableRow(
            "label/" + r.Key, new Dictionary<string, ViewNode>
            {
                ["name"] = document.InspectionField("label/" + r.Key, "Name", r.Name, Confidence.Inferred, r.AttributionText),
                ["category"] = new TextNode(r.Category ?? "Unknown"), ["id"] = new TextNode(r.Key) { Monospace = true },
            })).ToArray(), Collection.TotalCount, entries.Length, offset, limit);
    }

    private static string? Value(ReferenceRecord record, string key) => key switch
    { "id" => record.Key, "name" => record.Name, "category" => record.Category, _ => null };
}
