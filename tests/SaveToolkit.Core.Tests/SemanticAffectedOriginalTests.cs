using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticAffectedOriginalTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterTargetRetainsFailedAffectedImmutableOriginalAndReclaimsOnlyUnrelatedEvidence(bool optional)
    {
        using var current = new AffectedDocument(optional, original: false);
        var review = Create(optional);
        ISaveDocument Original() => new AffectedDocument(optional, original: true);
        review.EnsureOriginal(Original);
        var entry = review.CaptureState().Original!;
        review.CaptureTargetedOriginals(Original, ["A"], CancellationToken.None);
        current.A = 2;
        review.Observe([new("A", FieldValue.Of(999), FieldValue.Of(2))], false, _ => true);
        Assert.True(review.TryOriginal("A", out var originalA));
        Assert.Equal(FieldValue.Of(1), originalA);

        review.CaptureTargetedOriginals(Original, ["B"], CancellationToken.None);
        var after = review.CaptureState().Original!;

        Assert.Equal(FieldValue.Of(1), after.Fields["A"].Value);
        Assert.InRange(after.Bytes, 0, optional ? 8192 : 4096);
        if (optional)
        {
            Assert.False(after.Fields.ContainsKey("optional"));
            Assert.True(after.Fields["B"].MetadataPublished);
            Assert.False(after.Complete);
            Assert.Equal(1, after.OmittedCount);
        }
        else
        {
            Assert.Equal(entry.Bytes, after.Bytes);
            Assert.Equal(entry.Fields["B"].Path, after.Fields["B"].Path);
            Assert.Equal(entry.Fields["B"] with { Path = after.Fields["B"].Path }, after.Fields["B"]);
            Assert.Equal(entry.OmittedCount, after.OmittedCount);
        }
        var result = review.Query(Original, current, _ => { }, _ => { }, 1, CancellationToken.None);
        var change = Assert.Single(result.Changes, change => change.ValueId == "A");
        Assert.Equal(FieldValue.Of(1), change.OriginalValue);
        Assert.Equal(FieldValue.Of(2), change.CurrentValue);
        Assert.False(result.Complete);
        var state = review.CaptureState();
        Assert.Equal(["A", "B"], state.AffectedOriginalIds.Union(state.TargetedIds).Order(StringComparer.Ordinal));
        Assert.Contains("A", state.AffectedOriginalIds);
        output.WriteLine($"ENTRY={entry.Bytes}; retained={after.Bytes}; protected immutable A={after.Fields["A"].Value}; optional reclaim={optional}; omissions={after.OmittedCount}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterBatchFailurePreservesBaselineAndAffectedOwnershipAndCancellationRestoresPreflight(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var failLater = false;
        ISaveDocument Original() => new AffectedDocument(false, original: true)
        {
            OnResolve = ids =>
            {
                if (!failLater || !ids.Contains("late-199")) return;
                if (cancel) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
                throw new InvalidOperationException("Later batch failed");
            },
        };
        var review = new SemanticReview(new() { MaxOriginalBytes = 128_000 });
        review.EnsureOriginal(Original);
        review.CaptureTargetedOriginals(Original, ["A"], CancellationToken.None);
        var before = review.CaptureState();
        var rollback = review.CancellationRollback();
        failLater = true;
        var ids = Enumerable.Range(0, 200).Select(id => $"late-{id}").Prepend("B").ToArray();

        if (cancel)
        {
            Assert.Throws<OperationCanceledException>(() => review.CaptureTargetedOriginals(Original, ids, cancellation.Token));
            rollback(); // The same preflight rollback contract used by DocumentSession.
        }
        else review.CaptureTargetedOriginals(Original, ids, CancellationToken.None);

        var after = review.CaptureState();
        Assert.Equal(before.Original!.Bytes, after.Original!.Bytes);
        Assert.Equal(before.Original.OmittedCount, after.Original.OmittedCount);
        Assert.Equal(before.Original.Complete, after.Original.Complete);
        Assert.Equal(before.Original.Fields.Keys.Order(), after.Original.Fields.Keys.Order());
        foreach (var (id, field) in before.Original.Fields)
        {
            Assert.Equal(field.Path, after.Original.Fields[id].Path);
            Assert.Equal(field with { Path = after.Original.Fields[id].Path }, after.Original.Fields[id]);
        }
        Assert.Equal(before.TargetedIds, after.TargetedIds);
        Assert.Contains("A", after.AffectedOriginalIds);
        if (cancel)
        {
            Assert.Equal(before.AffectedOriginalIds, after.AffectedOriginalIds);
            Assert.Equal(before.CaptureDiagnostics, after.CaptureDiagnostics);
        }
        else
        {
            Assert.Contains("B", after.AffectedOriginalIds);
            Assert.NotEmpty(after.CaptureDiagnostics);
        }
    }

    private static SemanticReview Create(bool optional) => new(new() { MaxOriginalBytes = optional ? 8192 : 4096 });

    private sealed class AffectedDocument(bool optional, bool original) : BoundSaveDocument, IFieldMetadataDocument
    {
        public int A { get; set; } = 1;
        public Action<IReadOnlyList<string>>? OnResolve { get; init; }
        public override string Title => "Affected immutable original";
        public override ViewNode BuildView() => new TabsNode([
            new(new string('a', 1396), [new FieldNode("A", "A", IntegerSpec.Int32, FieldValue.Of(A))]),
            new("", optional
                ? [new FieldNode("B", "B", IntegerSpec.Int32, FieldValue.Of(1)),
                    new FieldNode("optional", "optional", new TextSpec(), FieldValue.Of(new string('x', 2000)))]
                : [new FieldNode("B", "B", IntegerSpec.Int32, FieldValue.Of(1))]),
        ]);
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> ids)
        {
            OnResolve?.Invoke(ids);
            if (original && ids.Contains("A")) throw new InvalidOperationException("Original A remains unavailable");
            return ids.Select(id =>
            {
                var metadata = new FieldMetadata(id, id, IntegerSpec.Int32)
                { Path = original && id == "B" ? [new string('b', 1900)] : null };
                RegisterField(metadata, () => FieldValue.Of(id == "A" ? A : 1), _ => { });
                return metadata;
            }).ToArray();
        }
    }
}
