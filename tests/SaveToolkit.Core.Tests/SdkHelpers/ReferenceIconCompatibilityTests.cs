using SaveToolkit.Core.References;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using static SaveToolkit.Core.Tests.SdkHelpers.ReferenceHelperFixture;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class ReferenceIconCompatibilityTests
{
    private const string MediaLicense = "Rights reserved by Bandai Namco Entertainment; used with attribution per owner decision 2026-10-29";
    private const string RecordCredit = "Source: https://example.org/item; Record authors, CC-BY-4.0, https://example.org/records (retrieved 2026-10-29)";
    private const string ImageCredit = "Image authors, Image licence, https://example.org/images (retrieved 2026-10-29)";

    [Theory]
    [InlineData(null, "Name a")]
    [InlineData("Whole-item thumbnail; appearance unknown", "Whole-item thumbnail; appearance unknown")]
    [InlineData("", "")]
    public void ExistingOverloadPreservesTheEntireImageNode(string? label, string expectedLabel)
    {
        Assert.Equal(new ImageNode("pack:icons/item.png", $"{expectedLabel}. {RecordCredit}. Icon: {ImageCredit}"),
            ReferenceViews.Icon(new Context(), Record("a"), label));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExistingOverloadRequiresBothBytesAndProvenance(bool missingBytes, bool missingProvenance)
    {
        var context = new Context { MissingImage = missingBytes, MissingProvenance = missingProvenance };
        var fallback = new TextNode("Unconfirmed") { Tone = Tone.Muted };
        var actual = ReferenceViews.Icon(context, Record("a"), fallback: fallback);
        if (missingBytes || missingProvenance)
        {
            Assert.Same(fallback, actual);
            Assert.Equal(new TextNode("No reference icon"), ReferenceViews.Icon(context, Record("a")));
        }
        else
        {
            Assert.Equal(new ImageNode("pack:icons/item.png", $"Name a. {RecordCredit}. Icon: {ImageCredit}"), actual);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NullAndDefaultOptionsMatchExistingOverload(bool missingBytes, bool missingProvenance)
    {
        var context = new Context { MissingImage = missingBytes, MissingProvenance = missingProvenance };
        var record = Record("a");
        var fallback = new TextNode("Unknown") { Tone = Tone.Muted };
        var expected = ReferenceViews.Icon(context, record, "Evidence caveat", fallback);
        Assert.Equal(expected, ReferenceViews.Icon(context, record, "Evidence caveat", fallback, options: null));
        Assert.Equal(expected, ReferenceViews.Icon(context, record, "Evidence caveat", fallback, new()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  Exact alt.\nPublisher notice; ü —  ")]
    public void ExactAltTextOverridesLabelWithoutTrimmingOrAppendingAttribution(string exactAlt)
    {
        var context = new Context();
        Assert.Equal(new ImageNode("pack:icons/item.png", exactAlt),
            ReferenceViews.Icon(context, Record("a"), "Ignored label", null, new() { ExactAltText = exactAlt }));
        Assert.Equal(["pack:icons/item.png"], context.ProvenanceRequests);
        Assert.Equal(new TextNode("No reference icon"), ReferenceViews.Icon(
            new Context { MissingImage = true }, Record("a"), null, null, new() { ExactAltText = exactAlt }));
        Assert.Equal(new TextNode("No reference icon"), ReferenceViews.Icon(
            new Context { MissingProvenance = true }, Record("a"), null, null, new() { ExactAltText = exactAlt }));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BaldursGate3OptionsMatchHistoricalNodeIncludingMissingBytes(bool missingBytes, bool missingProvenance)
    {
        var context = new Context { MissingImage = missingBytes, MissingProvenance = missingProvenance };
        var record = Record("a");
        var expected = BaldursGate3Icon(context, record);
        Assert.Equal(missingProvenance ? (ViewNode)new TextNode("No reference icon")
            : new ImageNode("pack:icons/item.png", $"Name a. {RecordCredit}. Icon: {ImageCredit}"), expected);
        Assert.Equal(expected, MigratedBaldursGate3Icon(context, record));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TalesOfAriseOptionsMatchHistoricalRolePublisherAndAssetIdOnlyPolicy(bool missingBytes, bool missingProvenance)
    {
        var context = new Context { MissingImage = missingBytes, MissingProvenance = missingProvenance };
        foreach (var role in new string?[] { "item icon", null, "" })
        {
            var record = Record("1") with
            {
                Name = "Apple Gel",
                AssetId = "talesofarise.catalog:icons/apple.webp",
                Attributes = role is null ? new Dictionary<string, string>() : new() { ["iconRole"] = role },
            };
            var expected = TalesOfAriseIcon(record);
            Assert.Equal(new ImageNode(record.AssetId, $"Apple Gel: {role ?? "game icon"}. {MediaLicense}"), expected);
            Assert.Equal(expected, MigratedTalesOfAriseIcon(context, record));
        }
        Assert.NotEmpty(context.ProvenanceRequests); // Register Sources even when exact alt text is supplied.
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PluginOptionsPreserveNullRecordAndMissingAssetFallbacks(bool missingRecord)
    {
        var context = new Context();
        var record = missingRecord ? null : Record("a") with { AssetId = null };
        Assert.Equal(new TextNode("No reference icon"), BaldursGate3Icon(context, record));
        Assert.Equal(BaldursGate3Icon(context, record), MigratedBaldursGate3Icon(context, record));
        Assert.Equal(new TextNode("—"), TalesOfAriseIcon(record));
        Assert.Equal(TalesOfAriseIcon(record), MigratedTalesOfAriseIcon(context, record));
        Assert.Empty(context.ProvenanceRequests);
    }

    [Fact]
    public void EmptyAssetIdIsPreservedLikeThePluginAdapters()
    {
        var context = new Context();
        var record = Record("a") with { AssetId = "" };
        Assert.Equal(BaldursGate3Icon(context, record), MigratedBaldursGate3Icon(context, record));
        Assert.Equal(TalesOfAriseIcon(record), MigratedTalesOfAriseIcon(context, record));
    }

    [Theory]
    [InlineData(ReferenceIconAvailability.RequireBytesAndProvenance)]
    [InlineData(ReferenceIconAvailability.RequireProvenance)]
    [InlineData(ReferenceIconAvailability.AssetIdOnly)]
    public void EveryPolicyRegistersTheActualImageDatasetInSources(ReferenceIconAvailability availability)
    {
        var plugin = new ItemsPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, ItemsPlugin.TitleId);
        var record = context.CatalogForHost("items")!.Find("1001")!;
        Assert.False(context.Datasets().Single(source => source.Dataset.Kind == ReferenceDatasetKind.Assets).Used);
        Assert.IsType<ImageNode>(ReferenceViews.Icon(context, record, null, null,
            new() { Availability = availability, ExactAltText = "Exact publisher notice" }));
        Assert.True(context.Datasets().Single(source => source.Dataset.Kind == ReferenceDatasetKind.Assets).Used);
    }

    [Fact]
    public void AssetIdOnlyStillNeedsProvenanceWhenAltTextMustBeComposed()
    {
        var context = new Context { MissingImage = true, MissingProvenance = true };
        var fallback = new TextNode("No attribution available");
        Assert.Same(fallback, ReferenceViews.Icon(context, Record("a"), null, fallback,
            new() { Availability = ReferenceIconAvailability.AssetIdOnly }));
    }

    [Fact]
    public void InvalidAvailabilityAndNullContextAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReferenceViews.Icon(new Context(), Record("a"), null, null,
            new() { Availability = (ReferenceIconAvailability)int.MaxValue }));
        Assert.Throws<ArgumentNullException>(() => ReferenceViews.Icon(null!, Record("a")));
        Assert.Throws<ArgumentNullException>(() => ReferenceViews.Icon(null!, Record("a"), null, null, new()));
    }

    private static ViewNode MigratedBaldursGate3Icon(IReferenceContext context, ReferenceRecord? record) =>
        ReferenceViews.Icon(context, record, label: null, fallback: null,
            options: new() { Availability = ReferenceIconAvailability.RequireProvenance });

    private static ViewNode MigratedTalesOfAriseIcon(IReferenceContext context, ReferenceRecord? record) =>
        ReferenceViews.Icon(context, record, label: null, fallback: new TextNode("—"), options: new()
        {
            Availability = ReferenceIconAvailability.AssetIdOnly,
            ExactAltText = record is null ? null
                : $"{record.Name}: {record.Attributes.GetValueOrDefault("iconRole") ?? "game icon"}. {MediaLicense}",
        });

    // Small compatibility fixtures copied from Bg3References.Icon and AriseAssets.Icon.
    // Keep their pre-migration behavior independent of the new options implementation.
    private static ViewNode BaldursGate3Icon(IReferenceContext context, ReferenceRecord? record)
    {
        var icon = ReferenceViews.Icon(context, record);
        if (icon is ImageNode) return icon;
        return record?.AssetId is { } asset && context.AssetProvenance(asset) is { } provenance
            ? new ImageNode(asset, $"{record.Name}. {record.AttributionText}. Icon: {provenance.Attribution.ToShortText()}")
            : icon;
    }

    private static ViewNode TalesOfAriseIcon(ReferenceRecord? record) => record?.AssetId is { } asset
        ? new ImageNode(asset, $"{record.Name}: {record.Attributes.GetValueOrDefault("iconRole") ?? "game icon"}. {MediaLicense}")
        : new TextNode("—");
}
