using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.Tests.SdkHelpers;

internal static class ReferenceHelperFixture
{
    internal static readonly ReferenceAttribution RecordAttribution = new("https://example.org/records", "Record authors", "CC-BY-4.0",
        "https://creativecommons.org/licenses/by/4.0/", new(2026, 10, 29), "None");
    internal static readonly ReferenceDatasetInfo Dataset = new("pack", "items", ReferenceDatasetKind.Records, RecordAttribution);
    internal static readonly ReferenceDatasetInfo ImageDataset = new("pack", "icons", ReferenceDatasetKind.Assets,
        RecordAttribution with { Author = "Image authors", SourceUrl = "https://example.org/images", License = "Image licence" });
    internal static readonly ReferencePackInfo Pack = new("pack", "1.0.0", "Fixture pack", ReferencePackOrigin.Embedded);

    internal static ReferenceRecord Record(string key) => new(key, "Name " + key)
    {
        Dataset = Dataset, AssetId = "pack:icons/item.png", Source = "https://example.org/item",
        Category = "Reference", Description = "Reference metadata only",
    };

    internal sealed class TestCatalog(IReadOnlyList<ReferenceRecord> records) : IReferenceCatalog
    {
        public string Id => "items";
        public ReferencePackInfo Pack => ReferenceHelperFixture.Pack;
        public ReferenceDatasetInfo Dataset => ReferenceHelperFixture.Dataset;
        public int Count => CountOverride ?? records.Count;
        public int? CountOverride { get; set; }
        public IReadOnlyList<string> Categories => ["Reference"];
        public List<ReferenceQuery> Queries { get; } = [];
        public Func<ReferenceQuery, ReferencePage>? SearchOverride { get; set; }
        public ReferenceRecord? Find(string key) => records.SingleOrDefault(record => string.Equals(record.Key, key, StringComparison.Ordinal));
        public ReferencePage Search(ReferenceQuery query)
        {
            Queries.Add(query);
            if (SearchOverride is { } search) return search(query);
            return new(records.Skip(query.Offset).Take(query.Limit).ToArray(), records.Count, query.Offset);
        }
    }

    internal sealed class Context : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs => [Pack];
        public bool MissingImage { get; init; }
        public bool MissingProvenance { get; init; }
        public List<string> ProvenanceRequests { get; } = [];
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => null;
        public AssetContent? GetAsset(string assetId) => MissingImage ? null : new("image/png", new byte[] { 1 });
        public ReferenceDatasetInfo? AssetProvenance(string assetId)
        {
            ProvenanceRequests.Add(assetId);
            return MissingProvenance ? null : ImageDataset;
        }
    }
}
