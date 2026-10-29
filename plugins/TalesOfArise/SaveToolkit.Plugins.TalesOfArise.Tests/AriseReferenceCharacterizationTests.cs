using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseReferenceCharacterizationTests
{
    private const string ItemsId = "items";
    private const string InspectItems = "references/inspect/items";

    [Fact]
    public void FreshContexts_KeepDatasetUsageIndependentAndReferenceIdentityConsistent()
    {
        var first = Assert.IsType<ReferenceContext>(AriseTestReferences.CreateContext());
        var second = Assert.IsType<ReferenceContext>(AriseTestReferences.CreateContext());
        Assert.NotSame(first, second);
        Assert.All(first.Datasets(), dataset => Assert.False(dataset.Used));
        Assert.All(second.Datasets(), dataset => Assert.False(dataset.Used));
        var apple = first.Catalog("items")!.Find("1")!;
        Assert.Equal("Apple Gel", apple.Name);
        Assert.NotNull(first.GetAsset(apple.AssetId!));
        Assert.Contains(first.Datasets(), dataset => dataset.Dataset.Id == "items" && dataset.Used);
        Assert.All(second.Datasets(), dataset => Assert.False(dataset.Used));
        var other = second.Catalog("items")!.Find("1")!;
        Assert.Equal((apple.Key, apple.Name, apple.AssetId, apple.Source, apple.AttributionText),
            (other.Key, other.Name, other.AssetId, other.Source, other.AttributionText));
        Assert.Equal(apple.Attributes.OrderBy(pair => pair.Key), other.Attributes.OrderBy(pair => pair.Key));
        Assert.Equal(first.Packs.Select(pack => (pack.Id, pack.Version)), second.Packs.Select(pack => (pack.Id, pack.Version)));
    }

    [Fact]
    public void InspectionPicker_PreservesLabelsFlagsInitialValueAndAttribution()
    {
        var context = AriseTestReferences.CreateContext();
        var catalog = context.Catalog(ItemsId)!;
        var record = catalog.Find("1")!;
        var action = AriseReferences.InspectionAction(catalog, record);
        AssertPicker(action, record, fixedRecord: true);

        var section = Assert.IsType<SectionNode>(new AriseReferences(context).CatalogSection(ItemsId));
        var picker = Assert.Single(section.Children.OfType<ActionNode>());
        AssertPicker(picker, catalog.Search(new() { Limit = 1 }).Items[0], fixedRecord: false);
        Assert.Equal(InspectItems, picker.ActionId);
    }

    [Fact]
    public void InspectionMessageAndOutcome_AreReadOnlyAndPreserveExactDetails()
    {
        var context = AriseTestReferences.CreateContext();
        var record = context.Catalog(ItemsId)!.Find("1")!;
        var bytes = new AriseTestContainer().Encrypt();
        using var document = new TalesOfArisePlugin().Open(
            MemorySaveSet.SingleFile(AriseTestContainer.FileName, bytes, AriseTestContainer.TitleId), context);
        var result = document.InvokeAction(InspectItems, new Dictionary<string, FieldValue> { ["record"] = FieldValue.Of(record.Key) });
        Assert.Equal($"{record.Name} · ID 1 · {record.Category}\n{record.Description}\n{record.AttributionText}\n" +
            $"Icon: {record.Attributes["iconSource"]}; {record.Attributes["iconRole"]}. {AriseAssets.MediaLicense}\n" +
            "Inspection only; save state is unknown and no bytes were changed.", result.Message);
        Assert.Empty(result.Changes);
        Assert.Empty(result.Warnings);
        Assert.Null(result.Revert);
        Assert.False(result.StructureChanged);
        Assert.True(result.Undoable);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("integer")]
    [InlineData("unknown")]
    [InlineData("case")]
    public void InvalidInspectionSelection_IsRejected(string mutation)
    {
        var references = new AriseReferences(AriseTestReferences.CreateContext());
        var parameters = mutation switch
        {
            "missing" => new Dictionary<string, FieldValue>(),
            "integer" => new() { ["record"] = FieldValue.Of(1L) },
            "unknown" => new() { ["record"] = FieldValue.Of("missing") },
            "case" => new() { ["Record"] = FieldValue.Of("1") },
            _ => throw new ArgumentException("Unknown mutation.", nameof(mutation)),
        };
        Assert.Throws<ArgumentException>(() => references.Inspect(InspectItems, parameters));
    }

    [Fact]
    public void MissingCatalog_RetainsFallbackAndUnavailableDiagnostic()
    {
        var references = new AriseReferences(IReferenceContext.Empty);
        var section = Assert.IsType<SectionNode>(references.CatalogSection(ItemsId));
        Assert.Equal(ItemsId, section.Title);
        var fallback = Assert.IsType<TextNode>(Assert.Single(section.Children));
        Assert.Equal("No matching reference pack is pinned. Raw save identities remain available.", fallback.Text);
        Assert.Equal(Tone.Muted, fallback.Tone);
        var error = Assert.Throws<KeyNotFoundException>(() => references.Inspect(InspectItems,
            new Dictionary<string, FieldValue> { ["record"] = FieldValue.Of("1") }));
        Assert.Equal("Tales of Arise catalog 'items' is unavailable.", error.Message);
    }

    [Fact]
    public void Icon_PreservesAssetRoleAndPublisherNotice_AndNullFallback()
    {
        var record = AriseTestReferences.CreateContext().Catalog(ItemsId)!.Find("1")!;
        var icon = Assert.IsType<ImageNode>(AriseAssets.Icon(record));
        Assert.Equal(record.AssetId, icon.AssetId);
        Assert.Equal($"{record.Name}: {record.Attributes["iconRole"]}. {AriseAssets.MediaLicense}", icon.Alt);
        Assert.Equal("—", Assert.IsType<TextNode>(AriseAssets.Icon(null)).Text);
        Assert.Equal("—", Assert.IsType<TextNode>(AriseAssets.Icon(record with { AssetId = null })).Text);
    }

    private static void AssertPicker(ActionNode action, ReferenceRecord record, bool fixedRecord)
    {
        Assert.Equal(fixedRecord ? "Inspect" : "Inspect a reference record", action.Label);
        Assert.False(action.Mutates);
        Assert.False(action.SupportsPlanning);
        Assert.Equal(Tone.Info, action.Tone);
        Assert.Equal($"Read-only reference details. {record.AttributionText}", action.Description);
        var parameter = Assert.Single(action.Parameters);
        Assert.Equal("record", parameter.ValueId);
        Assert.Equal("Reference record", parameter.Label);
        Assert.Equal(FieldValue.Of(record.Key), parameter.Value);
        var spec = Assert.IsType<ReferenceSpec>(parameter.Spec);
        Assert.Equal(ItemsId, spec.CatalogId);
        Assert.Equal(fixedRecord, spec.ReadOnly);
        Assert.False(spec.AllowCustom);
    }
}
