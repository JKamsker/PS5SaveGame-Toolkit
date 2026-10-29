using SaveToolkit.Core.References;

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
}
