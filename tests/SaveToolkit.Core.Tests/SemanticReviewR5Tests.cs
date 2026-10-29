using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticReviewR5Tests
{
    [Fact]
    public void TargetedCaptureBoundsAggregateIdsAndRetainsOnlyAuthoritativeTargets()
    {
        using var document = new UnresolvedMetadataDocument();
        var review = new SemanticReview(new());
        var ids = Enumerable.Range(0, FieldMetadataLimits.MaxClosureIds + 100)
            .Select(index => $"missing-{index}").ToArray();

        review.CaptureTargetedOriginals(() => document, ids, CancellationToken.None);
        review.CaptureTargetedOriginals(() => document, ids, CancellationToken.None);

        var state = review.CaptureState();
        Assert.Empty(state.TargetedIds);
        Assert.Equal(2 * ((FieldMetadataLimits.MaxClosureIds + FieldMetadataLimits.MaxBatchSize - 1)
            / FieldMetadataLimits.MaxBatchSize), document.ResolveCalls);
        Assert.InRange(state.CaptureDiagnostics.Count, 1, FieldMetadataLimits.MaxClosureIds + 1);
        Assert.Contains(state.CaptureDiagnostics, diagnostic => diagnostic.Contains("Omitted entries", StringComparison.Ordinal));
    }

    [Fact]
    public void OversizedNestedSpecIsIncompleteUntilAValidRetryAndNearBoundarySpecIsAdmitted()
    {
        using var current = new BudgetMetadataDocument(oversized: true);
        var review = new SemanticReview(new() { MaxOriginalBytes = 4096 });
        var oversizedSpec = new NullableSpec(new UnionSpec([new UnionOption("large", new string('x', 10_000), IntegerSpec.Int32)]));
        Assert.False(FieldSpecMetadata.TryEstimate(oversizedSpec, 4096, out _));
        Assert.False(FieldSpecMetadata.TryEstimate(current.ResolveFieldMetadata(["value"])[0].Spec, 4096, out _));

        review.CaptureTargetedOriginals(() => new BudgetMetadataDocument(oversized: true), ["value"], CancellationToken.None);
        Assert.NotEmpty(review.CaptureState().CaptureDiagnostics);
        Assert.False(review.Query(current, _ => { }, _ => { }, 1).Complete);

        current.Oversized = false;
        Assert.Empty(current.ApplyEdits([new ValueEdit("value", FieldValue.Of(2))]).Errors);
        review.CaptureTargetedOriginals(() => new BudgetMetadataDocument(oversized: false), ["value"], CancellationToken.None);

        var retry = review.Query(current, _ => { }, _ => { }, 2);
        Assert.True(retry.Complete);
        Assert.Equal("1", Assert.Single(retry.Changes).OriginalValue.ToString());
        Assert.Equal("2", retry.Changes[0].CurrentValue.ToString());
        Assert.True(retry.Changes[0].CanRevert);
    }

    private sealed class UnresolvedMetadataDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        public int ResolveCalls { get; private set; }
        public override string Title => "Unresolved metadata";
        public override ViewNode BuildView() => new TextNode("No fields");
        public override IReadOnlyList<FileOutput> Serialize() => [];

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            ResolveCalls++;
            return [];
        }
    }

    private sealed class BudgetMetadataDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private int _value = 1;
        public bool Oversized { get; set; }
        public override string Title => "Budget metadata";
        public override ViewNode BuildView() => new TextNode("No fields");

        public BudgetMetadataDocument(bool oversized)
        {
            Oversized = oversized;
            RegisterField("value", IntegerSpec.Int32, () => FieldValue.Of(_value), value => _value = value.AsInt32());
        }

        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)_value })];

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            if (!valueIds.Contains("value", StringComparer.Ordinal)) return [];
            FieldSpec spec = Oversized
                ? new NullableSpec(new UnionSpec([new UnionOption("large", new string('x', 10_000), IntegerSpec.Int32)]))
                : IntegerSpec.Int32;
            return [new FieldMetadata("value", "Value", spec)];
        }
    }
}
