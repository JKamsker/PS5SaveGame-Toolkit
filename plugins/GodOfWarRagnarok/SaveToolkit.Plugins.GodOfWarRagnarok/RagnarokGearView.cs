using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

/// <summary>Complete-model query projections with page-only read-only field bindings.</summary>
internal sealed class RagnarokGearView
{
    internal const string CollectionId = "gear";
    private readonly InMemoryCollection<Row> _collection;
    private sealed record Row(RagnarokGearRecord Gear, ReferenceRecord? Reference)
    {
        public string Key => $"gear/{Gear.Instance}";
        public string Name => Reference?.Name ?? $"Unknown gear ID 0x{Gear.Id:X16}";
        public string Level => Reference?.Attributes.GetValueOrDefault("level") ?? "Unknown";
        public decimal? NumericLevel => decimal.TryParse(Level, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    public RagnarokGearView(RagnarokDocument document, RagnarokRecordIndex index, RagnarokGearReferences references)
    {
        Row[]? rows = null;
        var columns = new QueryableColumn[]
        {
            new("instance", "Instance index (inferred)"), new("definition", "Definition (inferred)"),
            new("level", "Catalog level (inferred)"), new("parent", "Parent index (unknown role)"),
            new("slots", "Six-byte subrecord count (unknown role)"), new("source", "Attribution"),
        };
        var metadata = new QueryableCollection(CollectionId, "Gear instances · inferred definitions and catalog levels", columns, index.Gear.Records.Count)
        {
            Description = "Read-only framing inferred from a unique length-bounded block before wallets. " +
                "Catalog levels describe exact definition hashes; no separate upgrade scalar is established. " +
                "Parent indices and six-byte subrecords do not prove equipped or unlocked slots. " +
                "Core weapon levels, derived stats, dependencies and integrity repair remain unknown.",
        };
        _collection = new(metadata, () => rows ??= index.Gear.Records.Select(g => new Row(g, references.Find(g.Id))).ToArray(), row => row.Key,
        [
            Numeric("instance", r => r.Gear.Instance),
            new("definition") { SearchText = r => r.Name + " " + RagnarokReferences.Key(r.Gear.Id),
                FilterText = r => RagnarokReferences.Key(r.Gear.Id),
                Comparer = Comparer<Row>.Create((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name)) },
            new("level") { SearchText = r => r.Level, FilterText = r => r.Level,
                Comparer = Comparer<Row>.Create((a, b) => Nullable.Compare(a.NumericLevel, b.NumericLevel)) },
            Numeric("parent", r => r.Gear.Parent), Numeric("slots", r => r.Gear.SlotCount),
            Text("source", r => r.Reference?.AttributionText ?? "No sourced definition mapping"),
        ], row => Render(document, row), new() { DefaultComparer = Comparer<Row>.Create((a, b) => a.Gear.Instance.CompareTo(b.Gear.Instance)) });
    }

    public QueryableCollection Metadata => _collection.Metadata;
    public QueryableCollectionPage Query(QueryableCollectionQuery query) => _collection.Query(query);

    private static TableRow Render(RagnarokDocument document, Row row) => new(row.Key, new Dictionary<string, ViewNode>
    {
        ["instance"] = document.Number(row.Gear.Offset + sizeof(ulong), "Instance index (inferred)", Confidence.Inferred),
        ["definition"] = document.GearField(row.Key + "/definition", row.Name, RagnarokReferences.Key(row.Gear.Id), row.Reference,
            reference: row.Reference is not null),
        ["level"] = document.GearField(row.Key + "/catalog-level", "Catalog level (inferred)", row.Level,
            row.Level == "Unknown" ? null : row.Reference),
        ["parent"] = document.Number(row.Gear.Offset + sizeof(ulong) + sizeof(uint), "Parent index (unknown role)", Confidence.Unknown),
        ["slots"] = document.Number(row.Gear.Offset + sizeof(ulong) + 2 * sizeof(uint), "Six-byte subrecord count (unknown role)", Confidence.Unknown),
        ["source"] = new TextNode(row.Reference?.AttributionText ?? "No sourced definition mapping"),
    });

    private static InMemoryColumn<Row> Text(string key, Func<Row, string> value) => new(key)
    { SearchText = value, FilterText = value, Comparer = Comparer<Row>.Create((a, b) => StringComparer.OrdinalIgnoreCase.Compare(value(a), value(b))) };

    private static InMemoryColumn<Row> Numeric(string key, Func<Row, uint> value) => new(key)
    {
        SearchText = r => value(r).ToString(CultureInfo.InvariantCulture), FilterText = r => value(r).ToString(CultureInfo.InvariantCulture),
        Comparer = Comparer<Row>.Create((a, b) => value(a).CompareTo(value(b))),
    };
}
