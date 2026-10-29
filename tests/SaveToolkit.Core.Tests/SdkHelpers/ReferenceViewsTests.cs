using SaveToolkit.Core.References;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using static SaveToolkit.Core.Tests.SdkHelpers.ReferenceHelperFixture;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class ReferenceViewsTests
{
    [Fact]
    public void IconPreservesSeparateRecordAndImageAttribution_AndPluginEvidenceCaveats()
    {
        var context = new Context();
        var icon = Assert.IsType<ImageNode>(ReferenceViews.Icon(context, Record("raw/123"),
            "Whole-item thumbnail; not individual part appearance"));
        Assert.Equal("pack:icons/item.png", icon.AssetId);
        Assert.Contains("Whole-item thumbnail; not individual part appearance", icon.Alt);
        Assert.Contains("Record authors", icon.Alt);
        Assert.Contains("Image authors", icon.Alt);
        Assert.Contains("Image licence", icon.Alt);
        Assert.Contains("https://example.org/item", icon.Alt);
        Assert.Equal([icon.AssetId], context.ProvenanceRequests);
        Assert.Contains("Name raw/123", Assert.IsType<ImageNode>(ReferenceViews.Icon(context, Record("raw/123"))).Alt);
    }

    [Fact]
    public void IconRegistersActualImageDatasetInSources()
    {
        var plugin = new ItemsPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, ItemsPlugin.TitleId);
        var record = context.CatalogForHost("items")!.Find("1001")!;
        Assert.False(context.Datasets().Single(source => source.Dataset.Kind == ReferenceDatasetKind.Assets).Used);
        Assert.IsType<ImageNode>(ReferenceViews.Icon(context, record));
        Assert.True(context.Datasets().Single(source => source.Dataset.Kind == ReferenceDatasetKind.Assets).Used);
    }

    [Fact]
    public void MissingRecordImageOrProvenanceUsesTheSuppliedFallback()
    {
        var fallback = new TextNode("No confirmed reference image") { Tone = Tone.Muted };
        Assert.Same(fallback, ReferenceViews.Icon(new Context(), null, fallback: fallback));
        Assert.Same(fallback, ReferenceViews.Icon(new Context(), Record("a") with { AssetId = null }, fallback: fallback));
        Assert.Same(fallback, ReferenceViews.Icon(new Context { MissingProvenance = true }, Record("a"), fallback: fallback));
        Assert.Same(fallback, ReferenceViews.Icon(new Context { MissingImage = true }, Record("a"), fallback: fallback));
        Assert.IsType<TextNode>(ReferenceViews.Icon(IReferenceContext.Empty, Record("a")));
    }

    [Fact]
    public void InspectionActionIsNonMutating_AndKeepsExactRawIdentityAndEvidenceProse()
    {
        var record = Record("00AB/ß");
        var catalog = new TestCatalog([record]);
        var action = ReferenceInspection.Create(catalog, "inspect/raw", "Inspect", "record", "Reference record",
            "Reference only; ownership and upgrades unknown.", record.Key, fixedRecord: true)!;
        Assert.False(action.Mutates);
        Assert.False(action.SupportsPlanning);
        Assert.Contains("ownership and upgrades unknown", action.Description);
        Assert.Contains(record.AttributionText, action.Description);
        var parameter = Assert.Single(action.Parameters);
        var spec = Assert.IsType<ReferenceSpec>(parameter.Spec);
        Assert.True(spec.ReadOnly);
        Assert.False(spec.AllowCustom);
        Assert.Equal(catalog.Id, spec.CatalogId);
        Assert.Equal(record.Key, parameter.Value.AsString());
        var parameters = new Dictionary<string, FieldValue> { [parameter.ValueId] = parameter.Value };
        Assert.Same(record, ReferenceInspection.ResolveSelection(catalog, parameters, "record", record.Key));
        Assert.Equal(record.Key, parameters["record"].AsString());
        Assert.Equal(record, catalog.Find(record.Key));
        Assert.Empty(catalog.Queries); // Explicit initial keys use indexed lookup, never enumeration.
    }

    [Fact]
    public void InspectionHandlesOptionalAndEmptyCatalogs_AndChoosesOnlyOneInitialRecord()
    {
        Assert.Null(ReferenceInspection.Create(null, "inspect", "Inspect", "id", "Id", "Unknown ownership"));
        Assert.Null(ReferenceInspection.Create(new TestCatalog([]), "inspect", "Inspect", "id", "Id", "Unknown ownership"));
        var catalog = new TestCatalog([Record("a"), Record("b")]);
        var action = ReferenceInspection.Create(catalog, "inspect", "Inspect", "id", "Id", "Reference only")!;
        Assert.Equal("a", action.Parameters[0].Value.AsString());
        Assert.Equal(1, Assert.Single(catalog.Queries).Limit);
        Assert.Throws<ArgumentException>(() => ReferenceInspection.Create(catalog, "inspect", "Inspect", "id", "Id", "Reference only", "unknown"));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("wrong-name")]
    [InlineData("extra")]
    [InlineData("numeric")]
    [InlineData("unknown")]
    [InlineData("case")]
    [InlineData("fixed-record")]
    public void InspectionRejectsTamperedParametersAndNonMembers(string kind)
    {
        var catalog = new TestCatalog([Record("a"), Record("b")]);
        var parameters = new Dictionary<string, FieldValue> { ["record"] = FieldValue.Of("a") };
        switch (kind)
        {
            case "absent": parameters.Clear(); break;
            case "wrong-name": parameters.Clear(); parameters["other"] = FieldValue.Of("a"); break;
            case "extra": parameters["extra"] = FieldValue.Of("a"); break;
            case "numeric": parameters["record"] = FieldValue.Of(1L); break;
            case "unknown": parameters["record"] = FieldValue.Of("missing"); break;
            case "case": parameters["record"] = FieldValue.Of("A"); break;
            case "fixed-record": parameters["record"] = FieldValue.Of("b"); break;
        }
        Assert.Throws<ArgumentException>(() => ReferenceInspection.ResolveSelection(catalog, parameters, "record",
            kind == "fixed-record" ? "a" : null));
        Assert.Throws<InvalidOperationException>(() => ReferenceInspection.ResolveSelection(null, parameters, "record"));
    }
}
