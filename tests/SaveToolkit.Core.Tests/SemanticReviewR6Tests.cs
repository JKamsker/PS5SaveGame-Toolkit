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

    private static string Id(int index) => $"field-{index:0000}";

    private sealed class MetadataBatchDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private readonly Dictionary<string, int> _values = new(StringComparer.Ordinal);
        private readonly FieldPresentation? _presentation;

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
        public int ResolveCalls { get; private set; }
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
}
