using System.Text.Json;
using SaveToolkit.Core.Tests.SdkHelpers;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.Tests.References;

public sealed class EmbeddedPackComparisonTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsolatedPackIsComparedEvenWhenAnotherPackWinsSelection(bool windowsNames)
    {
        var first = EmbeddedReferenceRejectionTests.Resources(PackBuilder.WithItems("First", "test.first"));
        var second = EmbeddedReferenceRejectionTests.Resources(
            PackBuilder.WithItems("Second", "test.second") with { Version = "2.0.0" });
        var files = first.Files.Select(pair => KeyValuePair.Create(
            pair.Key.Replace("/fixture/", "/first/", StringComparison.Ordinal), pair.Value))
            .Concat(second.Files.Select(pair => KeyValuePair.Create(
                pair.Key.Replace("/fixture/", "/second/", StringComparison.Ordinal), pair.Value)))
            .ToDictionary(pair => windowsNames ? pair.Key.Replace('/', '\\') : pair.Key, pair => pair.Value);
        var assembly = new EmbeddedReferenceRejectionTests.ResourceAssembly(files);
        Assert.Equal("Second", EmbeddedReferenceContext.Load(assembly).Catalog("items")!.Find("1001")!.Name);

        var name = EmbeddedPackSnapshotTests.Manifests(assembly).Single(pack => pack.Manifest.GetProperty("id").GetString() == "test.first").Name;
        var isolated = EmbeddedReferenceContext.Load(new SinglePackResourceAssembly(assembly, name));
        Assert.Equal("test.first", Assert.Single(isolated.Packs).Id);
        Assert.Equal("First", isolated.Catalog("items")!.Find("1001")!.Name);
    }

    [Fact]
    public void RecordDifferenceNamesThePackAndFirstDifferingKey()
    {
        var records = new[] { ReferenceHelperFixture.Record("same"), ReferenceHelperFixture.Record("first-difference"),
            ReferenceHelperFixture.Record("later-difference") };
        var changed = records.Select((record, index) => index == 0 ? record : record with { Name = "Changed" }).ToArray();
        var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => EmbeddedReferenceEquivalenceTests.CompareCatalog(
            new ReferenceHelperFixture.TestCatalog(records), new ReferenceHelperFixture.TestCatalog(changed),
            "Pack 'test.pack', catalog 'items'"));
        Assert.Contains("Pack 'test.pack'", error.Message, StringComparison.Ordinal);
        Assert.Contains("record #1", error.Message, StringComparison.Ordinal);
        Assert.Contains("first-difference", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("later-difference", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRecordNamesTheFirstMissingKey()
    {
        var records = new[] { ReferenceHelperFixture.Record("same"), ReferenceHelperFixture.Record("missing") };
        var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => EmbeddedReferenceEquivalenceTests.CompareCatalog(
            new ReferenceHelperFixture.TestCatalog(records), new ReferenceHelperFixture.TestCatalog(records[..1]), "Pack 'test.pack'"));
        Assert.Contains("Pack 'test.pack'", error.Message, StringComparison.Ordinal);
        Assert.Contains("missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetDifferenceNamesThePackAndAsset()
    {
        using var manifest = JsonDocument.Parse("""{"datasets":[{"kind":"assets","files":[{"path":"icons/item.png"}]}]}""");
        var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => EmbeddedReferenceEquivalenceTests.CompareAssets(
            new ReferenceHelperFixture.Context(), new ReferenceHelperFixture.Context { MissingImage = true }, manifest.RootElement, "test.pack"));
        Assert.Contains("Pack 'test.pack'", error.Message, StringComparison.Ordinal);
        Assert.Contains("test.pack:icons/item.png", error.Message, StringComparison.Ordinal);
    }
}
