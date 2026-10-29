using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

/// <summary>Loads attributed embedded records once for direct callers without a Core reference context.</summary>
internal static class RagnarokReferenceLoader
{
    private static readonly EmbeddedReferenceContext Embedded =
        EmbeddedReferenceContext.Load(typeof(RagnarokReferenceLoader).Assembly);
    private static readonly int MaxRecords = new EmbeddedReferencePackLimits().MaxRecordsPerCatalog;
    private const string Build = "PS5 revision 0x00513040";

    public static IReadOnlyDictionary<string, ReferenceRecord> Load(string packId)
    {
        var pack = Embedded.Packs.Single(p => p.Id == packId);
        var records = new Dictionary<string, ReferenceRecord>(StringComparer.Ordinal);
        foreach (var dataset in pack.Datasets.Where(d => d.Kind == ReferenceDatasetKind.Records))
        {
            var catalog = Embedded.Catalog(dataset.Id, Build)
                ?? throw new InvalidOperationException("Embedded Ragnarök catalog is missing.");
            if (catalog.Pack.Id != packId)
                throw new InvalidOperationException("Embedded Ragnarök source preference changed; review the join policy.");
            // Direct fallback records historically exposed attribution without catalog summary metadata.
            var provenance = catalog.Dataset with { Title = null, ItemCount = 0 };
            // Icons and quantity limits remain supplied by the host, as in the original fallback.
            foreach (var record in ReferenceCatalogReader.ReadBounded(catalog, new(), MaxRecords))
                records.Add(record.Key, record with { Dataset = provenance, AssetId = null, Limit = null });
        }
        return records;
    }
}
