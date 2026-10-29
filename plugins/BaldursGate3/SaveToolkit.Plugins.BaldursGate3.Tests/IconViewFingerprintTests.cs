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
    // SDK-05 compatibility baseline, refreshed for the intentional #40 gold discovery view.
    private const string WithProvenance = "b616f04f7e1735a828e5c55f70996b3c24981432bf8dc0fdef3927c561591334";
    private const string WithoutProvenance = "21cdabbd47a36192b703ac200831287c854e36bc34b7db5979a39b4c67f4aa4f";

    [Theory]
    [InlineData(true, true, WithProvenance)]
    [InlineData(false, true, WithProvenance)]
    [InlineData(false, false, WithoutProvenance)]
    public void SampleViewsKeepTheirAttributedIconAndGoldDiscoveryFingerprint(bool hasBytes, bool hasProvenance, string expected)
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
            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            Assert.True(expected == actual, $"Expected view fingerprint {expected}; actual {actual}.");

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
