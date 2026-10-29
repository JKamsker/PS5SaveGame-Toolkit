using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ImageCellTests
{
    private const string PartLabel = "Whole-item reference thumbnail; not individual part appearance";
    private static readonly ReferenceDatasetInfo Records = Dataset("records", ReferenceDatasetKind.Records, "Record authors");
    private static readonly ReferenceDatasetInfo Images = Dataset("images", ReferenceDatasetKind.Assets, "Image authors");

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ImageCell_KeepsAssetIdentityEvidenceCaveatAndProvenance(bool part, bool source)
    {
        var context = new Context();
        var record = Record() with { Source = source ? "https://example.org/item" : null };
        var image = Assert.IsType<ImageNode>(GearboxImages.Cell(context, record, part));
        var label = part ? PartLabel : record.Name;
        Assert.Equal(record.AssetId, image.AssetId);
        Assert.Equal($"{label}. {record.AttributionText}. Icon: {Images.Attribution.ToShortText()}", image.Alt);
        Assert.Equal([image.AssetId], context.ProvenanceRequests);
        Assert.Equal([image.AssetId], context.AssetRequests);
    }

    [Theory]
    [InlineData("record")]
    [InlineData("asset")]
    [InlineData("provenance")]
    [InlineData("bytes")]
    public void MissingReference_UsesExactFallback(string missing)
    {
        var context = new Context { MissingProvenance = missing == "provenance", MissingImage = missing == "bytes" };
        var record = missing == "record" ? null : Record() with { AssetId = missing == "asset" ? null : "pack:item.png" };
        Assert.Equal(new TextNode("Image unavailable"), GearboxImages.Cell(context, record));
    }

    private static ReferenceRecord Record() => new("raw/item", "Item name") { Dataset = Records, AssetId = "pack:item.png" };

    private static ReferenceDatasetInfo Dataset(string id, ReferenceDatasetKind kind, string author) => new("pack", id, kind,
        new("https://example.org/" + id, author, "CC-BY-4.0", "https://creativecommons.org/licenses/by/4.0/",
            new(2026, 10, 29), "None"));

    private sealed class Context : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs => [];
        public bool MissingProvenance { get; init; }
        public bool MissingImage { get; init; }
        public List<string> ProvenanceRequests { get; } = [];
        public List<string> AssetRequests { get; } = [];
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => null;
        public AssetContent? GetAsset(string assetId)
        {
            AssetRequests.Add(assetId);
            return MissingImage ? null : new("image/png", new byte[] { 1 });
        }
        public ReferenceDatasetInfo? AssetProvenance(string assetId)
        {
            ProvenanceRequests.Add(assetId);
            return MissingProvenance ? null : Images;
        }
    }
}
