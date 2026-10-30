using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticStructuralOriginalTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HiddenStructuralRetryUsesOnlyImmutableOriginalEvidence(bool originallyPresent)
    {
        const string identity = "hidden";
        var opens = 0;
        ISaveDocument OpenOriginal()
        {
            opens++;
            return new HiddenDocument(originallyPresent, 1);
        }
        using var current = new HiddenDocument(present: true, value: 2);
        var review = new SemanticReview(new());
        review.EnsureOriginal(OpenOriginal);
        Assert.Equal(1, opens);
        Assert.False(review.TryOriginal(identity, out _));

        // A structural report is deliberately not a trustworthy immutable old-value witness.
        review.Observe([new(identity, FieldValue.Of(999), FieldValue.Of(2))], structural: true, _ => false);
        Assert.False(review.TryOriginal(identity, out _));
        var result = review.Query(OpenOriginal, current, _ => { }, _ => { }, 1, CancellationToken.None);

        Assert.Equal(2, opens);
        Assert.DoesNotContain(result.Changes, change => change.OriginalValue == FieldValue.Of(999));
        if (originallyPresent)
        {
            var change = Assert.Single(result.Changes);
            Assert.Equal(identity, change.ValueId);
            Assert.True(change.OriginalExists);
            Assert.True(change.CurrentExists);
            Assert.Equal(FieldValue.Of(1), change.OriginalValue);
            Assert.Equal(FieldValue.Of(2), change.CurrentValue);
            Assert.True(change.CanRevert);
            Assert.True(result.Complete);
            Assert.True(review.TryOriginal(identity, out var original));
            Assert.Equal(FieldValue.Of(1), original);
            Assert.Contains(identity, review.CaptureState().TargetedIds);
        }
        else
        {
            Assert.False(result.Complete);
            Assert.False(review.TryOriginal(identity, out _));
            Assert.DoesNotContain(result.Changes, change => change.OriginalExists);
            Assert.DoesNotContain(identity, review.CaptureState().TargetedIds);
            Assert.NotEmpty(review.CaptureState().CaptureDiagnostics);
        }
    }

    private sealed class HiddenDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private readonly bool _present;

        public HiddenDocument(bool present, int value)
        {
            _present = present;
            if (present) RegisterField(new FieldMetadata("hidden", "Hidden canonical", IntegerSpec.Int32),
                () => FieldValue.Of(value), _ => { });
        }

        public override string Title => "Hidden structural consequence";
        public override ViewNode BuildView() => new TextNode("No rendered field evidence");
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
            => _present ? base.ResolveFieldMetadata(valueIds) : [];
    }
}
