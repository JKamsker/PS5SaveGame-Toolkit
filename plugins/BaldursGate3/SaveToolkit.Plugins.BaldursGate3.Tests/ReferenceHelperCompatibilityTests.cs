using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class ReferenceHelperCompatibilityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IconKeepsSeparateRecordAndImageAttributionEvenWhenOnlyProvenanceIsAvailable(bool hasBytes)
    {
        var (_, _, pinned) = ReferenceViewTests.References();
        var record = pinned.Catalog(Bg3References.Items)!.Search(new() { Limit = 1 }).Items[0];
        var provenance = pinned.AssetProvenance(record.AssetId!)! with
        {
            Attribution = record.Dataset.Attribution with
            {
                Author = "Separate image authors", License = "Separate image licence",
            },
        };
        var references = new Bg3References(new ImageContext(pinned, provenance, hasBytes));
        Assert.Equal(new ImageNode(record.AssetId!,
            $"{record.Name}. {record.AttributionText}. Icon: {provenance.Attribution.ToShortText()}"), references.Icon(record));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingRecordAssetIdOrProvenanceKeepsTheExactFallback(bool hasBytes)
    {
        var (_, _, pinned) = ReferenceViewTests.References();
        var record = pinned.Catalog(Bg3References.Items)!.Search(new() { Limit = 1 }).Items[0];
        var references = new Bg3References(pinned);
        Assert.Equal(new TextNode("No reference icon"), references.Icon(null));
        Assert.Equal(new TextNode("No reference icon"), references.Icon(record with { AssetId = null }));
        Assert.Equal(new TextNode("No reference icon"), new Bg3References(
            new ImageContext(pinned, null, hasBytes)).Icon(record));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  Label with punctuation.  ")]
    public void AltTextKeepsTheExactRecordNameAndRecordSource(string name)
    {
        var (_, _, pinned) = ReferenceViewTests.References();
        var record = pinned.Catalog(Bg3References.Items)!.Search(new() { Limit = 1 }).Items[0] with { Name = name };
        var provenance = pinned.AssetProvenance(record.AssetId!)!;
        var references = new Bg3References(pinned);
        Assert.Equal(new ImageNode(record.AssetId!,
            $"{name}. {record.AttributionText}. Icon: {provenance.Attribution.ToShortText()}"), references.Icon(record));
        var withoutSource = record with { Source = null };
        Assert.Equal(new ImageNode(record.AssetId!,
            $"{name}. {withoutSource.AttributionText}. Icon: {provenance.Attribution.ToShortText()}"), references.Icon(withoutSource));
    }

    [Fact]
    public void DescriptionsRetainRawIdentifiersAttributionAndCandidateEvidenceCaveats()
    {
        var (_, _, pinned) = ReferenceViewTests.References();
        var record = pinned.Catalog(Bg3References.Items)!.Search(new() { Limit = 1 }).Items[0];
        var field = new FieldNode("raw", "Raw id", new TextSpec { ReadOnly = true }, FieldValue.Of(record.Key));
        var known = Bg3References.Describe(field, record, record.Key);
        Assert.Equal(Confidence.Inferred, known.Confidence);
        Assert.Equal($"{record.Description} Raw identifier: {record.Key}. {record.AttributionText}. Pack {record.Dataset.PackId}; candidate label only, not a live identity/ownership proof.", known.Description);
        var unknown = Bg3References.Describe(field, null, "unresolved");
        Assert.Equal(Confidence.Unknown, unknown.Confidence);
        Assert.Equal("Unresolved raw identifier: unresolved. No matching build-agnostic reference record.", unknown.Description);
    }

    private sealed class ImageContext(IReferenceContext inner, ReferenceDatasetInfo? provenance, bool hasBytes) : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs => inner.Packs;
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => inner.Catalog(catalogId, gameBuild);
        public AssetContent? GetAsset(string assetId) => hasBytes ? inner.GetAsset(assetId) : null;
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => provenance;
    }
}
