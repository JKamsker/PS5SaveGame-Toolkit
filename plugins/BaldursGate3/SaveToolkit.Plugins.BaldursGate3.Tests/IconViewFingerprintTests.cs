using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class IconViewFingerprintTests
{
    // Captured from the local compatibility adapter before SDK-05 adoption.
    private const string WithProvenance = "2290fea9ceb33494d516c504e7b0f3df780643a2b7aa28cbe628a5dd6e3ef0a6";
    private const string WithoutProvenance = "69575604999f9c6a91d7c00f6953d9a5cac8da0ae73e17cf819d8f099d51cbcc";

    [Theory]
    [InlineData(true, true, WithProvenance)]
    [InlineData(false, true, WithProvenance)]
    [InlineData(false, false, WithoutProvenance)]
    public void SampleViewsKeepTheirPreSdk05Fingerprint(bool hasBytes, bool hasProvenance, string expected)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            // Overview byte counts and other display values use the current culture.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var (plugin, _, pinned) = ReferenceViewTests.References();
            var context = new ImageContext(pinned, hasBytes, hasProvenance);
            using var document = Assert.IsType<BaldursGate3Document>(plugin.Open(SampleLiveNewAgeTests.Save(), context));
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Append(document.BuildView());
            foreach (var collection in document.Collections)
            {
                hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(collection));
                // Include every page, so later inventory/class/spell icons cannot escape the baseline.
                for (var offset = 0; offset < collection.TotalCount; offset += QueryableCollectionLimits.MaxPageSize)
                {
                    var page = document.Query(collection.Id, new QueryableCollectionQuery
                    {
                        Offset = offset, Limit = QueryableCollectionLimits.MaxPageSize,
                    });
                    hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(page));
                    if (collection.Id.EndsWith("/books", StringComparison.Ordinal))
                        foreach (var row in page.Rows)
                            foreach (var node in document.ExpandNode(Assert.IsType<SectionNode>(row.Cells["details"]).LazyId!))
                                Append(node);
                }
            }
            Assert.Equal(expected, Convert.ToHexStringLower(hash.GetHashAndReset()));

            void Append(ViewNode node) => hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(node));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private sealed class ImageContext(IReferenceContext inner, bool hasBytes, bool hasProvenance) : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs => inner.Packs;
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => inner.Catalog(catalogId, gameBuild);
        public AssetContent? GetAsset(string assetId) => hasBytes ? inner.GetAsset(assetId) : null;
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => hasProvenance ? inner.AssetProvenance(assetId) : null;
    }
}
