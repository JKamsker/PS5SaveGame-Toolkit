using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticReviewR9Tests
{
    [Fact]
    public void PendingMetadataFailureOwnsOneOmissionAndRollbackRestoresIt()
    {
        var coverage = new SemanticReviewCoverage();
        coverage.RecordOmissions(2, "unrelated omission");
        using var document = new RetryDocument();
        var snapshot = new SemanticSnapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal)
        {
            ["value"] = new SemanticField("value", "Value", [], FieldValue.Of(1), true,
                CanonicalValueId: "value", Spec: IntegerSpec.Int32 with { ReadOnly = true }),
        }, true, 0);
        var fail = true;
        var capturing = false;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var failed = SemanticOriginalMetadata.Enrich(() =>
            {
                if (fail) throw new InvalidOperationException($"transient failure {attempt}");
                return document;
            }, snapshot, 16 * 1024 * 1024, [], forceAttempt: true, ref capturing, CancellationToken.None);
            Assert.False(failed.Applied);
            coverage.RecordOriginalFailures([], failed.Diagnostic!);
        }
        Assert.Equal(3, coverage.OmittedCount);

        var state = coverage.CaptureState();
        coverage.RecordOmissions(4, "temporary cancellation work");
        coverage.Restore(state);
        Assert.Equal(3, coverage.OmittedCount);

        fail = false;
        var repaired = SemanticOriginalMetadata.Enrich(() => document, snapshot, 16 * 1024 * 1024, [],
            forceAttempt: true, ref capturing, CancellationToken.None);
        Assert.True(repaired.Applied);
        coverage.ClearMetadataAttemptFailure();
        Assert.Equal(2, coverage.OmittedCount);
        Assert.Contains(coverage.Diagnostics(), message => message.Contains("unrelated omission", StringComparison.Ordinal));
        Assert.DoesNotContain(coverage.Diagnostics(), message => message.Contains("transient failure", StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyOversizedNestedSpecIsRejectedBeforeSnapshotCopy()
    {
        var options = new CountingOptions(Enumerable.Range(0, 32)
            .Select(index => new UnionOption($"option-{index}", new string('L', 96), IntegerSpec.Int32)));
        using var document = new LegacySpecDocument(new UnionSpec(options));
        var capture = new SemanticSnapshotCapture(new DocumentSessionOptions { MaxOriginalBytes = 512 });

        var original = capture.Capture(document, _ => { }, _ => { }, CancellationToken.None);
        var current = capture.Capture(document, _ => { }, _ => { }, CancellationToken.None);

        Assert.False(original.Complete);
        Assert.False(current.Complete);
        Assert.Empty(original.Fields);
        Assert.Empty(current.Fields);
        Assert.Equal(0, options.EnumerationCount);
    }

    [Fact]
    public void ProviderDiagnosticsAreBoundedAcrossAffectedIds()
    {
        var coverage = new SemanticReviewCoverage();
        var huge = new string('X', 100_000);
        for (var index = 0; index < 8; index++)
            coverage.RecordFailure($"field-{index}", huge);

        Assert.All(coverage.Diagnostics(), message => Assert.InRange(message.Length, 0, SemanticDiagnostics.MaxMessageChars));
        Assert.Equal(8, coverage.OmittedCount);
    }

    private sealed class LegacySpecDocument(UnionSpec spec) : BoundSaveDocument
    {
        public override string Title => "Legacy spec";
        public override ViewNode BuildView() => new SectionNode("Values", [new FieldNode("value", "Value", spec, FieldValue.Union("option-0", FieldValue.Of(1)))]);
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }

    private sealed class RetryDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        public override string Title => "Retry document";
        public override ViewNode BuildView() => new TextNode("No fields");
        public override IReadOnlyList<FileOutput> Serialize() => [];

        public RetryDocument() => RegisterField(new FieldMetadata("value", "Value", IntegerSpec.Int32 with { ReadOnly = true }),
            () => FieldValue.Of(1));
    }

    private sealed class CountingOptions(IEnumerable<UnionOption> options) : IReadOnlyList<UnionOption>
    {
        private readonly UnionOption[] _options = options.ToArray();
        public int EnumerationCount { get; private set; }
        public int Count => _options.Length;
        public UnionOption this[int index] => _options[index];
        public IEnumerator<UnionOption> GetEnumerator()
        {
            EnumerationCount++;
            return ((IEnumerable<UnionOption>)_options).GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
