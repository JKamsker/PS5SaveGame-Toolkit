using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Optional document-pinned packs override individual records; fallback entries remain available.</summary>
internal sealed class ReferenceData(IReferenceContext context, Ff7Schema schema)
{
    // Match the host's default per-catalog pack limit without silently publishing a partial index.
    private const int MaxCatalogRecords = 250_000;

    public AssetContent? GetAsset(string assetId) => context.GetAsset(assetId) ?? EmbeddedCatalogs.Asset(assetId);
    public ReferenceDatasetInfo? AssetProvenance(string assetId) => context.AssetProvenance(assetId) ?? EmbeddedCatalogs.AssetProvenance(assetId);
    private readonly Dictionary<string, IReadOnlyList<ReferenceRecord>> _catalogs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<uint, ReferenceRecord>> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<(bool Materia, uint Id), string> _descriptions = new();
    private readonly Dictionary<(string Catalog, uint Id), string> _sources = new();

    public string? ContentIdentity => context.ContentIdentity;

    public string Item(uint id) => Record("ff7r-items", id)?.Name ?? Ff7Catalog.Item(schema, id);
    public string Materia(uint id) => Record("ff7r-materia", id)?.Name ?? Ff7Catalog.Materia(schema, id);
    public string Weapon(uint id) => Record("ff7r-weapons", id)?.Name ?? $"Unknown weapon 0x{id:X}";
    public string Character(int index) => context.Catalog("ff7r-characters", schema.Name)?
        .Find(index.ToString(CultureInfo.InvariantCulture))?.Name ?? Ff7Catalog.Character(schema, index);

    public string CharacterSource(int index) => context.Catalog("ff7r-characters", schema.Name)?
        .Find(index.ToString(CultureInfo.InvariantCulture))?.AttributionText ?? EmbeddedCatalogs.Attribution;

    public string Source(string catalogId, uint id)
    {
        if (_sources.TryGetValue((catalogId, id), out var source)) return source;
        var record = Record(catalogId, id);
        if (record is null) return EmbeddedCatalogs.Attribution;
        source = record.AttributionText;
        _sources.Add((catalogId, id), source);
        return source;
    }

    public ReferenceRecord? Record(string catalogId, uint id)
    {
        _ = Records(catalogId);
        return _byId[catalogId].GetValueOrDefault(id);
    }

    public IReadOnlyList<ReferenceRecord> Records(string catalogId)
    {
        if (_catalogs.TryGetValue(catalogId, out var cached))
        {
            return cached;
        }

        var values = EmbeddedCatalogs.Records(schema, catalogId).ToDictionary(record => record.Key, StringComparer.Ordinal);
        if (context.Catalog(catalogId, schema.Name) is { } catalog)
        {
            foreach (var record in ReferenceCatalogReader.ReadBounded(catalog,
                new ReferenceQuery { Limit = ReferenceQuery.MaxLimit }, MaxCatalogRecords))
            {
                if (uint.TryParse(record.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                {
                    values[id.ToString(CultureInfo.InvariantCulture)] = record;
                }
            }
        }

        var records = values.Values.OrderBy(record => uint.Parse(record.Key, CultureInfo.InvariantCulture)).ToArray();
        _catalogs.Add(catalogId, records);
        _byId.Add(catalogId, records.ToDictionary(record => uint.Parse(record.Key, CultureInfo.InvariantCulture)));
        return records;
    }

    public string Describe(bool materia, uint id)
    {
        if (_descriptions.TryGetValue((materia, id), out var description)) return description;
        var record = Record(materia ? "ff7r-materia" : "ff7r-items", id);
        description = record is null ? "Unknown type; raw bytes are preserved." :
            $"{record.Description} {Source(materia ? "ff7r-materia" : "ff7r-items", id)}";
        _descriptions.Add((materia, id), description);
        return description;
    }

    // Retained lazy endpoint for old sessions and bounded explorer tests.
    public TableNode CatalogPage(bool materia, int page)
    {
        const int pageSize = 128;
        var entries = Records(materia ? "ff7r-materia" : "ff7r-items");
        if (page < 0 || page >= (entries.Count + pageSize - 1) / pageSize)
        {
            throw new KeyNotFoundException("Unknown FFVII catalog page.");
        }

        return new TableNode([new("id", "ID"), new("name", "Name"), new("category", "Category"), new("source", "Source")],
            [.. entries.Skip(page * pageSize).Take(pageSize).Select(record => new TableRow(
                $"catalog/{(materia ? "materia" : "items")}/{uint.Parse(record.Key, CultureInfo.InvariantCulture):X8}",
                new Dictionary<string, ViewNode>(StringComparer.Ordinal)
                {
                    ["id"] = new TextNode(record.Key), ["name"] = new TextNode(record.Name),
                    ["category"] = new TextNode(record.Category ?? "Unknown"),
                    ["source"] = new TextNode(record.AttributionText),
                }))]) { Description = EmbeddedCatalogs.Attribution };
    }
}
