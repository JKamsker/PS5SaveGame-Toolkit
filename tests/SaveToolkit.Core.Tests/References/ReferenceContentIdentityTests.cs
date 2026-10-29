using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.Tests.References;

public sealed class ReferenceContentIdentityTests
{
    [Fact]
    public void SameDisplayMetadataWithDifferentContentGetsDifferentFingerprint()
    {
        var options = new ReferencePackOptions { StorageRoot = TestZips.TempDir() };
        var store = new ReferencePackStore([], options);
        var first = store.Import(PackBuilder.WithItems().Zip(), "first.zip");
        var second = store.Import(PackBuilder.WithItems("Different").Zip(), "second.zip");

        Assert.Equal(first.Info.Id, second.Info.Id);
        Assert.Equal(first.Info.Version, second.Info.Version);
        Assert.Equal(first.Info.Locale, second.Info.Locale);
        Assert.NotEqual(first.Info.ContentFingerprint, second.Info.ContentFingerprint);
    }

    [Fact]
    public void ManifestMetadataAndListedHashesArePartOfIdentity()
    {
        var options = new ReferencePackOptions { StorageRoot = TestZips.TempDir() };
        var first = new ReferencePackStore([], options).Import(PackBuilder.WithItems().Zip(), "first.zip");
        var overlayBuilder = PackBuilder.WithItems();
        overlayBuilder = overlayBuilder with { Mutate = manifest => { manifest["description"] = "overlay"; } };
        var changedManifest = new ReferencePackStore([], new ReferencePackOptions { StorageRoot = TestZips.TempDir() })
            .Import(overlayBuilder.Zip(), "overlay.zip");

        Assert.NotEqual(first.Info.ContentFingerprint, changedManifest.Info.ContentFingerprint);
        Assert.StartsWith("sha256:", first.Info.ContentFingerprint);
        Assert.StartsWith("sha256:", changedManifest.Info.ContentFingerprint);
    }

    [Fact]
    public void SelectionLocaleAndOriginArePartOfContextIdentity()
    {
        var fingerprint = "sha256:" + new string('a', 64);
        var embedded = new ReferencePackInfo("items", "1.0", "Items", ReferencePackOrigin.Embedded) { ContentFingerprint = fingerprint, Locale = "en" };
        var user = embedded with { Origin = ReferencePackOrigin.User };
        var english = new SnapshotContext([embedded], ["en"]);
        var german = new SnapshotContext([embedded], ["de-DE"]);
        var overlay = new SnapshotContext([user], ["en"]);

        Assert.NotEqual(english.Identity, german.Identity);
        Assert.NotEqual(english.Identity, overlay.Identity);
        Assert.Equal(english.Identity, new SnapshotContext([embedded], ["en"]).Identity);
    }

    private sealed class SnapshotContext(IReadOnlyList<ReferencePackInfo> packs, IReadOnlyList<string> locales) : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs { get; } = packs;
        public IReadOnlyList<string> PreferredLocales { get; } = locales;
        public string? Identity => ((IReferenceContext)this).ContentIdentity;
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => null;
        public AssetContent? GetAsset(string assetId) => null;
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => null;
    }
}
