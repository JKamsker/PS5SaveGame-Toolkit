using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticReviewR6Tests
{
    [Fact]
    public void FailedCanonicalEnrichmentLeavesTheLastGoodSnapshotUntouched()
    {
        using var document = new MetadataBatchDocument(401) { FailAfterFirstBatch = true };
        var fields = Enumerable.Range(0, 401).ToDictionary(index => Id(index), index =>
            new SemanticField(Id(index), "Legacy", ["legacy", Id(index)], FieldValue.Of(1), false,
                Spec: IntegerSpec.Int32), StringComparer.Ordinal);
        var snapshot = new SemanticSnapshot(fields, true, fields.Count * 300)
        { QueryWarnings = ["baseline warning"] };
        var before = snapshot.Clone();

        var failed = SemanticTargetedAuthority.ApplyCanonicalMetadata(document, snapshot, long.MaxValue, CancellationToken.None);

        Assert.False(failed.Applied);
        Assert.NotEmpty(failed.IncompleteIds);
        Assert.Equal(before.Bytes, snapshot.Bytes);
        Assert.Equal(before.Complete, snapshot.Complete);
        Assert.Equal(before.QueryWarnings, snapshot.QueryWarnings);
        Assert.Equal(before.Fields.Keys, snapshot.Fields.Keys);
        foreach (var id in before.Fields.Keys)
        {
            var expected = before.Fields[id];
            var actual = snapshot.Fields[id];
            Assert.Equal(expected.Label, actual.Label);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(expected.CanonicalValueId, actual.CanonicalValueId);
            Assert.Equal(expected.BindingKind, actual.BindingKind);
            Assert.True(FieldSpecMetadata.Agree(expected.Spec!, actual.Spec!));
        }

        document.FailAfterFirstBatch = false;
        var retry = SemanticTargetedAuthority.ApplyCanonicalMetadata(document, snapshot, long.MaxValue, CancellationToken.None);

        Assert.True(retry.Applied);
        Assert.All(snapshot.Fields.Values, field => Assert.StartsWith("Canonical", field.Label, StringComparison.Ordinal));
        Assert.All(snapshot.Fields.Values, field => Assert.Equal("canonical", field.Path[0]));
    }

    [Fact]
    public void TargetedCaptureStopsMetadataGettersAfterTheAggregateBudgetIsExhausted()
    {
        using var document = new MetadataBatchDocument(FieldMetadataLimits.MaxBatchSize,
            presentation: new FieldPresentation { Warning = new string('w', 120) });
        var review = new SemanticReview(new() { MaxOriginalBytes = 4096 });

        review.CaptureTargetedOriginals(() => document,
            Enumerable.Range(0, FieldMetadataLimits.MaxBatchSize).Select(Id).ToArray(), CancellationToken.None);

        Assert.InRange(document.GetValueCalls, 0, FieldMetadataLimits.MaxBatchSize - 1);
        Assert.NotEmpty(review.CaptureState().CaptureDiagnostics);
    }

    [Fact]
    public void AuthorityEnrichmentStopsBeforeCopyingUnadmittedBatchMetadata()
    {
        using var document = new MetadataBatchDocument(FieldMetadataLimits.MaxBatchSize,
            presentation: new FieldPresentation { Warning = new string('w', 120) });
        var fields = Enumerable.Range(0, FieldMetadataLimits.MaxBatchSize).ToDictionary(index => Id(index), index =>
            new SemanticField(Id(index), "Legacy", [], FieldValue.Of(1), false, Spec: IntegerSpec.Int32), StringComparer.Ordinal);
        var snapshot = new SemanticSnapshot(fields, true, fields.Count * 300);
        var before = snapshot.Clone();

        var result = SemanticTargetedAuthority.ApplyCanonicalMetadata(document, snapshot,
            snapshot.Bytes + 4096, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.InRange(document.GetValueCalls, 0, FieldMetadataLimits.MaxBatchSize - 1);
        Assert.Equal(before.Bytes, snapshot.Bytes);
        Assert.Equal(before.QueryWarnings, snapshot.QueryWarnings);
        Assert.Equal(before.Fields.Keys, snapshot.Fields.Keys);
    }

    [Fact]
    public void InitialMetadataSequenceStopsAtTheBoundBeforeProviderDispatch()
    {
        using var document = new MetadataBatchDocument(0);
        var accesses = 0;
        IEnumerable<string> ids()
        {
            for (var index = 0; index < FieldMetadataLimits.MaxClosureIds + 10_000; index++)
            {
                accesses++;
                yield return Id(index);
            }
        }

        Assert.Throws<InvalidOperationException>(() => new FieldMetadataIndex(document).ResolveAll(ids()));
        Assert.Equal(0, document.ResolveCalls);
        Assert.Equal(FieldMetadataLimits.MaxClosureIds + 1, accesses);
    }

    [Fact]
    public void UnionEstimatorCountsLargeFirstOptionOnceAtTheAdmissionBoundary()
    {
        var first = new EnumSpec(Enumerable.Range(0, 32)
            .Select(index => new EnumOption(index.ToString(), new string('a', 24))).ToArray());
        var spec = new UnionSpec([
            new UnionOption("large", "Large", first),
            new UnionOption("small", "Small", IntegerSpec.Int32),
        ]);

        Assert.True(FieldSpecMetadata.TryEstimate(spec, long.MaxValue, out var estimate));
        Assert.True(FieldSpecMetadata.TryEstimate(spec, estimate, out _));
        Assert.False(FieldSpecMetadata.TryEstimate(spec, estimate - 1, out _));
        Assert.True(FieldSpecMetadata.Agree(spec, FieldSpecMetadata.Snapshot(spec)));
    }

    [Fact]
    public void FailedOriginalMetadataRetriesOnTheNextReviewAndClearsOnlyThatFailure()
    {
        var fail = true;
        var calls = 0;
        using var current = new RetryMetadataDocument(() => fail);
        var review = new SemanticReview(new());

        review.CaptureTargetedOriginals(() => new RetryMetadataDocument(() => { calls++; return fail; }), ["hidden"],
            CancellationToken.None);
        Func<ISaveDocument> openOriginal = () => new RetryMetadataDocument(() => { calls++; return fail; });
        Assert.False(review.Query(openOriginal, current, _ => { }, _ => { }, 1, CancellationToken.None).Complete);

        fail = false;
        current.SetValue(2);
        var retry = review.Query(openOriginal, current, _ => { }, _ => { }, 2, CancellationToken.None);

        Assert.True(retry.Complete);
        var change = Assert.Single(retry.Changes);
        Assert.Equal("hidden", change.ValueId);
        Assert.Equal("Canonical hidden", change.Label);
        Assert.Equal("1", change.OriginalValue.ToString());
        Assert.Equal("2", change.CurrentValue.ToString());
        Assert.True(change.OriginalExists);
        Assert.True(change.CurrentExists);
        Assert.True(change.CanRevert);
        Assert.Contains("hidden", review.CaptureState().TargetedIds);
        Assert.Empty(review.CaptureState().CaptureDiagnostics);
        Assert.True(review.TryOriginal("hidden", out var original));
        Assert.Equal("1", original.ToString());
    }

    [Fact]
    public void FailedCurrentTargetedMetadataContributesBoundedOmissionEvidence()
    {
        using var document = new RetryMetadataDocument(() => true);
        var snapshot = new SemanticSnapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal), true, 0);
        var result = SemanticTargetedReview.Capture(new SemanticTargetedCapture(4096), document, snapshot,
            ["hidden"], revokeOnFailure: true, (_, _) => { }, CancellationToken.None);

        Assert.False(result.Complete);
        Assert.Equal(1, snapshot.OmittedCount);
        Assert.True(snapshot.OmittedCountIsLowerBound);
        Assert.False(snapshot.Complete);
    }

    [Fact]
    public void RepairedRenderedMetadataClearsOnlyTransientCoverageState()
    {
        var failures = 1;
        using var current = new RenderedRetryDocument(() => failures-- > 0);
        var review = new SemanticReview(new());
        Func<ISaveDocument> openOriginal = () => new RenderedRetryDocument(() => failures-- > 0);

        var first = review.Query(openOriginal, current, _ => { }, _ => { }, 1, CancellationToken.None);
        Assert.False(first.Complete);
        Assert.NotEmpty(first.Warnings);

        current.SetValue(2);
        var repaired = review.Query(openOriginal, current, _ => { }, _ => { }, 2, CancellationToken.None);

        Assert.True(repaired.Complete);
        var change = Assert.Single(repaired.Changes);
        Assert.Equal("1", change.OriginalValue.ToString());
        Assert.Equal("2", change.CurrentValue.ToString());
        Assert.Empty(repaired.Warnings);
        Assert.Empty(review.CaptureState().CaptureDiagnostics);
    }

    [Fact]
    public void RepairedMissingRenderedMetadataClearsTransientOmission()
    {
        // The initial targeted capture and canonical enrichment each request metadata, then
        // the first review retry must also remain incomplete before the next retry repairs it.
        var omissions = 3;
        using var current = new RenderedRetryDocument(static () => false);
        var review = new SemanticReview(new());
        Func<ISaveDocument> openOriginal = () => new RenderedRetryDocument(static () => false,
            () => omissions-- > 0);

        review.CaptureTargetedOriginals(openOriginal, ["hidden"], CancellationToken.None);
        Assert.False(review.Query(openOriginal, current, _ => { }, _ => { }, 1, CancellationToken.None).Complete);
        var repaired = review.Query(openOriginal, current, _ => { }, _ => { }, 2, CancellationToken.None);

        Assert.True(repaired.Complete);
        Assert.Empty(repaired.Warnings);
        Assert.Empty(review.CaptureState().CaptureDiagnostics);
    }

    private static string Id(int index) => $"field-{index:0000}";

    private sealed class MetadataBatchDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private readonly Dictionary<string, int> _values = new(StringComparer.Ordinal);
        private readonly FieldPresentation? _presentation;
        public int ResolveCalls { get; private set; }

        public MetadataBatchDocument(int count, FieldPresentation? presentation = null)
        {
            _presentation = presentation;
            for (var index = 0; index < count; index++)
            {
                var id = Id(index);
                _values[id] = 1;
                RegisterField(id, IntegerSpec.Int32, () => { GetValueCalls++; return FieldValue.Of(_values[id]); },
                    value => _values[id] = value.AsInt32());
            }
        }

        public bool FailAfterFirstBatch { get; set; }
        public int GetValueCalls { get; private set; }
        public override string Title => "Metadata batch";
        public override ViewNode BuildView() => new TextNode("No rendered fields");
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            ResolveCalls++;
            if (FailAfterFirstBatch && ResolveCalls > 1)
                throw new InvalidOperationException("second metadata batch failed");
            return valueIds.Select(id => new FieldMetadata(id, $"Canonical {id}", IntegerSpec.Int32)
            {
                Path = ["canonical", id], Presentation = _presentation,
            }).ToArray();
        }
    }

    private sealed class RetryMetadataDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private readonly Func<bool> _failResolver;
        private int _value = 1;
        public int ResolveCalls { get; private set; }

        public RetryMetadataDocument(Func<bool> failResolver)
        {
            _failResolver = failResolver;
            RegisterField(new FieldMetadata("hidden", "Canonical hidden", IntegerSpec.Int32)
            {
                Presentation = new FieldPresentation { Confidence = Confidence.Inferred, Warning = "Review" },
                Path = ["canonical", "hidden"],
            }, () => FieldValue.Of(_value), value => _value = value.AsInt32());
        }

        public override string Title => "Retry metadata";
        public void SetValue(int value) => _value = value;
        public override ViewNode BuildView() => new TextNode("No rendered fields");
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            ResolveCalls++;
            if (_failResolver()) throw new InvalidOperationException("transient metadata resolver failure");
            return base.ResolveFieldMetadata(valueIds);
        }
    }

    private sealed class RenderedRetryDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private int _value = 1;
        private readonly Func<bool> _omitResolver;

        public RenderedRetryDocument()
            : this(static () => false, static () => false) { }

        public RenderedRetryDocument(Func<bool> failResolver, Func<bool>? omitResolver = null)
        {
            this.failResolver = failResolver;
            _omitResolver = omitResolver ?? (static () => false);
            RegisterField("hidden", IntegerSpec.Int32, () => FieldValue.Of(_value), value => _value = value.AsInt32());
        }

        private Func<bool> failResolver { get; }
        public override string Title => "Rendered retry metadata";
        public void SetValue(int value) => _value = value;
        public override ViewNode BuildView() => new SectionNode("Root", [new FieldNode("hidden", "Legacy hidden", IntegerSpec.Int32, FieldValue.Of(_value))]);
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            if (failResolver()) throw new InvalidOperationException("transient rendered metadata failure");
            if (_omitResolver()) return [];
            return valueIds.Select(id => new FieldMetadata(id, "Canonical hidden", IntegerSpec.Int32)
            {
                Path = ["canonical", id],
            }).ToArray();
        }
    }
}
