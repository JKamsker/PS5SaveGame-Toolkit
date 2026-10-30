using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticWalkCompositionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeutralAndTargetedWalksPreserveEquivalentValuesNavigationAndCoverage(bool incomplete)
    {
        var neutral = Run(targeted: false, incomplete);
        var targeted = Run(targeted: true, incomplete);
        Assert.Equal(JsonSerializer.Serialize(neutral, MetadataJson.Options), JsonSerializer.Serialize(targeted, MetadataJson.Options));
        Assert.Equal(!incomplete, neutral.Complete);
        var change = Assert.Single(neutral.Changes);
        Assert.Equal("primary", change.ValueId);
        Assert.Equal(FieldValue.Of(1), change.OriginalValue);
        Assert.Equal(FieldValue.Of(2), change.CurrentValue);
        Assert.True(change.CanRevert);
        Assert.True(change.CanNavigate);
        Assert.Contains("section:Values", change.Path);
        Assert.Equal("alias", Assert.Single(change.Aliases!).ValueId);
        if (incomplete) Assert.NotEmpty(neutral.Warnings);
        else Assert.Empty(neutral.Warnings);
    }

    private static SemanticChangeLog Run(bool targeted, bool incomplete)
    {
        var review = new SemanticReview(new());
        review.EnsureOriginal(() => new WalkDocument(incomplete));
        if (targeted) review.CaptureTargetedOriginals(() => new WalkDocument(incomplete), ["primary"], CancellationToken.None);
        using var current = new WalkDocument(incomplete) { Value = 2 };
        return review.Query(() => new WalkDocument(incomplete), current, _ => { }, _ => { }, 1, CancellationToken.None);
    }

    private sealed class WalkDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private readonly bool _incomplete;
        public int Value { get; set; } = 1;
        public WalkDocument(bool incomplete)
        {
            _incomplete = incomplete;
            RegisterField(new("primary", "Primary", IntegerSpec.Int32), () => FieldValue.Of(Value), value => Value = value.AsInt32());
            RegisterField(new FieldMetadata("alias", "Alias", IntegerSpec.Int32 with { ReadOnly = true })
            { CanonicalValueId = "primary", BindingKind = FieldBindingKind.Derived }, () => FieldValue.Of(Value));
        }
        public override string Title => "Walk";
        public override ViewNode BuildView() => new SectionNode("Values", _incomplete
            ? [Field("primary", "Primary"), Field("alias", "Alias"), new SectionNode("Unavailable", []) { LazyId = "unavailable" }]
            : [Field("primary", "Primary"), Field("alias", "Alias")]);
        public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => throw new InvalidOperationException("synthetic unavailable branch");
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
