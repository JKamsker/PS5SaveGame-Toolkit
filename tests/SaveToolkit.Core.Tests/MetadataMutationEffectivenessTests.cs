using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class MetadataMutationEffectivenessTests
{
    [Theory]
    [InlineData("duplicate")]
    [InlineData("derived-primary")]
    [InlineData("readonly-primary")]
    [InlineData("old-value")]
    public void InvalidPlansAreRejectedBeforeApplication(string kind)
    {
        using var document = new MetadataProbe();
        var change = new PlannedChange("first", "First", FieldValue.Of(1L), FieldValue.Of(2L));
        var changes = kind switch
        {
            "duplicate" => new[] { change, change },
            "derived-primary" => [change with { Derived = true }],
            "readonly-primary" => [change with { ValueId = "readonly" }],
            _ => [change with { OldValue = FieldValue.Of(3L) }],
        };

        Assert.Throws<InvalidOperationException>(() => FieldMetadataPlanning.Validate(
            new ViewIndex(document), document, new ActionPlan { Changes = changes }));
    }

    [Fact]
    public void MetadataMustPromiseAReadableLiveBinding()
    {
        using var document = new MetadataProbe();
        document.Descriptors = [new("unbound", "Unbound", IntegerSpec.UInt8)];

        Assert.Throws<KeyNotFoundException>(() => new ViewIndex(document).ResolveMetadata(["unbound"]));
    }

    [Fact]
    public void RemovedDescriptorsRetirePreviouslyCachedMetadata()
    {
        using var document = new MetadataProbe();
        var index = new ViewIndex(document);
        Assert.NotNull(index.Metadata("first"));
        document.Descriptors = [];
        Assert.Null(index.Metadata("first"));
        Assert.DoesNotContain("first", index.BindingIds);
    }

    [Fact]
    public void ResolvingMetadataDetectsDriftAgainstAnAlreadyRenderedField()
    {
        using var document = new MetadataProbe();
        var index = new ViewIndex(document);
        index.Add(new FieldNode("first", "First", IntegerSpec.UInt8, FieldValue.Of(1L)));
        document.Descriptors = [new("first", "First", new IntegerSpec(0, 10))];

        Assert.Throws<InvalidOperationException>(() => index.ResolveMetadata(["first"]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReplayPreservesForwardOrderAndReversesUndoOrder(bool undo)
    {
        var index = new ViewIndex();
        index.AddAll([new FieldNode("first", "First", IntegerSpec.UInt8, FieldValue.Of(1L)),
            new FieldNode("second", "Second", IntegerSpec.UInt8, FieldValue.Of(3L))]);
        ValueChange[] changes = [new("first", FieldValue.Of(1L), FieldValue.Of(2L)),
            new("second", FieldValue.Of(3L), FieldValue.Of(4L))];

        var edits = FieldMetadataReplay.Edits(index, changes, undo);

        string[] expectedIds = undo ? ["second", "first"] : ["first", "second"];
        int[] expectedValues = undo ? [3, 1] : [2, 4];
        Assert.Equal(expectedIds, edits.Select(edit => edit.ValueId));
        Assert.Equal(expectedValues, edits.Select(edit => edit.Value.AsInt32()));
    }

    [Fact]
    public void ReplayRequiresMetadataOrARenderedFallbackForEveryChangedField()
    {
        using var document = new MetadataProbe();
        var index = new ViewIndex(document);
        ValueChange[] changes = [new("missing", FieldValue.Of(1L), FieldValue.Of(2L))];
        Assert.Throws<InvalidOperationException>(() => FieldMetadataReplay.Edits(index, changes, useOld: true));

        index.Add(new FieldNode("legacy", "Legacy", IntegerSpec.UInt8, FieldValue.Of(1L)));
        Assert.Equal("legacy", Assert.Single(FieldMetadataReplay.Edits(index,
            [changes[0] with { ValueId = "legacy" }], useOld: false)).ValueId);
    }

    [Fact]
    public void BindingInventoryIncludesEveryKindAndClearRetiresAllCachedIds()
    {
        using var document = new MetadataProbe();
        var index = new ViewIndex(document);
        index.ResolveMetadata(["first"]);
        index.AddAll([new FieldNode("legacy", "Legacy", IntegerSpec.UInt8, FieldValue.Of(1L)),
            new ActionNode("action", "Action"), SectionNode.Lazy("Lazy", "lazy"),
            new HexNode("blob", 1), new ImageNode("asset", "Asset")]);
        Assert.Equal(["action", "first", "lazy", "legacy"], index.BindingIds.Order());
        foreach (var id in new[] { "first", "legacy", "action", "lazy" }) Assert.True(index.KnowsBinding(id));
        Assert.False(index.KnowsBinding("missing"));
        Assert.True(index.KnowsBlob("blob"));
        Assert.True(index.KnowsAsset("asset"));

        index.Clear();

        Assert.Empty(index.BindingIds);
        Assert.Empty(index.Specs);
        Assert.False(index.KnowsLazy("lazy"));
        Assert.Null(index.Action("action"));
        Assert.False(index.KnowsBlob("blob"));
        Assert.False(index.KnowsAsset("asset"));
    }

    [Fact]
    public void TabsIndexTheirChildFieldsAndEmptyFieldIdsAreRejected()
    {
        var index = new ViewIndex();
        index.Add(new TabsNode([new("Tab", [new FieldNode("field", "Field", IntegerSpec.UInt8, FieldValue.Of(1L))])]));
        Assert.Equal(IntegerSpec.UInt8, index.Spec("field"));
        Assert.Throws<InvalidOperationException>(() => index.Add(new FieldNode("", "Invalid", IntegerSpec.UInt8, FieldValue.Of(1L))));
    }

    private sealed class MetadataProbe : BoundSaveDocument, IFieldMetadataDocument
    {
        public IReadOnlyList<FieldMetadata> Descriptors { get; set; } =
            [new("first", "First", IntegerSpec.UInt8),
                new("readonly", "Read only", IntegerSpec.UInt8 with { ReadOnly = true })];
        public override string Title => "Metadata probe";

        public MetadataProbe()
        {
            RegisterField("first", IntegerSpec.UInt8, () => FieldValue.Of(1L), _ => { });
            RegisterField("readonly", IntegerSpec.UInt8, () => FieldValue.Of(1L));
        }

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds) =>
            Descriptors.Where(field => valueIds.Contains(field.ValueId, StringComparer.Ordinal)).ToArray();
        public override ViewNode BuildView() => new SectionNode(Title, []);
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
