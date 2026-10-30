using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticAdmissionCloseoutTests(ITestOutputHelper output)
{
    [Fact]
    public void ExactFullSameIdentityReplacementNeedsNoSecondDescriptorCharge()
    {
        using var document = new MetadataDocument();
        var field = Field("A", canonical: true);
        var snapshot = Snapshot(field);
        var limit = snapshot.Bytes;

        var result = new SemanticTargetedCapture(limit).Capture(document, snapshot, ["A"], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("A", Assert.Single(result.CapturedIds));
        Assert.Equal(limit, snapshot.Bytes);
        Assert.Equal(SemanticSnapshot.Estimate(snapshot.Fields["A"]), snapshot.Bytes);
        output.WriteLine($"ENTRY={limit}; same-ID growth=0; staging cap={limit}; retained={snapshot.Bytes}; getter calls={document.GetterCalls}");
    }

    [Fact]
    public void ShrinkingOneIdentityCannotFundANewIdentity()
    {
        using var document = new MetadataDocument();
        var field = Field("A", canonical: true) with { Path = [new string('x', 1000)] };
        var snapshot = Snapshot(field);
        var before = snapshot.Bytes;
        document.PathFor = id => id == "A" ? [] : [new string('y', 600)];

        var result = new SemanticTargetedCapture(before).Capture(document, snapshot, ["A", "B"], CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, document.GetterCalls);
        Assert.Equal(before, snapshot.Bytes);
        Assert.Equal(field, snapshot.Fields["A"]);
        Assert.Single(snapshot.Fields);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterBatchFailureOrCancellationRetainsSnapshotBeforeAnyGetter(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        using var document = new MetadataDocument
        {
            OnResolve = ids =>
            {
                if (!ids.Contains("200")) return;
                if (cancel) cancellation.Cancel();
                else throw new InvalidOperationException("late resolver failure");
            },
        };
        var field = Field("retained", canonical: true);
        var snapshot = Snapshot(field);
        var before = snapshot.Bytes;
        var capture = new SemanticTargetedCapture(1_000_000);
        var ids = Enumerable.Range(0, 201).Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();

        if (cancel)
            Assert.Throws<OperationCanceledException>(() => capture.Capture(document, snapshot, ids, cancellation.Token));
        else Assert.False(capture.Capture(document, snapshot, ids, cancellation.Token).Succeeded);

        Assert.Equal(0, document.GetterCalls);
        Assert.Equal(before, snapshot.Bytes);
        Assert.Equal(field, snapshot.Fields["retained"]);
        Assert.Single(snapshot.Fields);
    }

    [Fact]
    public void FullTemporaryDescriptorPayloadStillRejectsBeforeGetter()
    {
        using var document = new MetadataDocument { PathFor = _ => [new string('x', 1000)] };
        var field = Field("A", canonical: true);
        var snapshot = Snapshot(field);

        var result = new SemanticTargetedCapture(snapshot.Bytes).Capture(document, snapshot, ["A"], CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, document.GetterCalls);
        Assert.Equal(field, snapshot.Fields["A"]);
    }

    [Fact]
    public void TargetedGrowthCanReclaimOnlyUnrelatedOptionalLegacyEvidence()
    {
        using var document = new MetadataDocument { PathFor = _ => [new string('n', 100)] };
        var a = Field("A", canonical: false);
        var optional = Field("optional", canonical: false) with { Value = FieldValue.Of(new string('x', 1000)) };
        var snapshot = Snapshot(a, optional);
        var limit = snapshot.Bytes;

        var result = new SemanticTargetedCapture(limit).Capture(document, snapshot, ["A"], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("A", Assert.Single(result.CapturedIds));
        Assert.True(snapshot.Fields["A"].MetadataPublished);
        Assert.False(snapshot.Fields.ContainsKey("optional"));
        Assert.False(snapshot.Complete);
        Assert.Equal(1, snapshot.OmittedCount);
        Assert.InRange(snapshot.Bytes, 0, limit);
        Assert.Equal(SemanticSnapshot.Estimate(snapshot.Fields["A"]), snapshot.Bytes);
        output.WriteLine($"ENTRY={limit}; optional reclaimed={SemanticSnapshot.Estimate(optional)}; retained={snapshot.Bytes}; protected A original={snapshot.Fields["A"].Value}; omissions={snapshot.OmittedCount}");
    }

    [Fact]
    public void GetterFailureDoesNotPublishStagedLegacyReclamation()
    {
        using var document = new MetadataDocument { PathFor = _ => [new string('n', 100)], FailGetter = true };
        var a = Field("A", canonical: false);
        var optional = Field("optional", canonical: false) with { Value = FieldValue.Of(new string('x', 1000)) };
        var snapshot = Snapshot(a, optional);
        var before = snapshot.Bytes;

        var result = new SemanticTargetedCapture(before).Capture(document, snapshot, ["A"], CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(before, snapshot.Bytes);
        Assert.Equal(2, snapshot.Fields.Count);
        Assert.Equal(a, snapshot.Fields["A"]);
        Assert.Equal(optional, snapshot.Fields["optional"]);
        Assert.True(snapshot.Complete);
        Assert.Equal(0, snapshot.OmittedCount);
        output.WriteLine($"ENTRY={before}; getter failure rolls back optional reclamation, fields, completeness and omissions.");
    }

    [Fact]
    public void CanonicalClosureCannotBorrowTheAliasesRetainedCredit()
    {
        using var document = new MetadataDocument { Alias = true };
        var field = Field("A", canonical: true) with { ReadOnly = true, Spec = IntegerSpec.Int32 with { ReadOnly = true } };
        var snapshot = Snapshot(field);

        var result = new SemanticTargetedCapture(snapshot.Bytes).Capture(document, snapshot, ["A"], CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, document.GetterCalls);
        Assert.Equal(field, Assert.Single(snapshot.Fields).Value);
    }

    [Fact]
    public void ActualContextCostIsReservedOnBothSnapshotsWithoutTrimmingChangedEvidence()
    {
        var a = Field("A", canonical: true);
        var optional = Field("optional", canonical: false) with { Value = FieldValue.Of(new string('x', 1000)) };
        var original = Snapshot(a, optional);
        var current = Snapshot(a with { Value = FieldValue.Of(2) }, optional);
        var limit = Math.Max(original.Bytes, current.Bytes);
        var context = new SemanticMemberContext("character", "character.dat");
        var change = new SemanticChange("A", "A", [], FieldValue.Of(1), FieldValue.Of(2), true, true, true)
        {
            CanonicalValueId = "A",
            Aliases = [new("projection", "Projection", [])],
        };
        var calls = 0;

        var result = SemanticContextAdmission.Resolve(_ =>
        {
            calls++;
            return new Dictionary<string, SemanticMemberContext> { ["A"] = context };
        }, [change], original, current, limit, CancellationToken.None);

        Assert.True(result.Complete);
        Assert.Equal(1, calls);
        Assert.True(SemanticContextBudget.TryEstimate(context, out var cost));
        Assert.Equal(2 * cost, result.AcceptedBytes);
        Assert.InRange(original.Bytes + result.AcceptedBytes, 0, limit);
        Assert.InRange(current.Bytes + result.AcceptedBytes, 0, limit);
        Assert.Equal(1, original.Fields["A"].Value.AsInt32());
        Assert.Equal(2, current.Fields["A"].Value.AsInt32());
        Assert.False(original.Complete);
        Assert.False(current.Complete);
        output.WriteLine($"ENTRY original/current={limit}; changed-context reservation={result.AcceptedBytes} (2 projections); retained={original.Bytes}/{current.Bytes}; original/current changed values=1/2; omissions={original.OmittedCount}/{current.OmittedCount}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProtectedContextCapacityAndCancellationKeepBothSnapshotsUnchanged(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var a = Field("A", canonical: true);
        var protectedField = Field("protected", canonical: false) with { Value = FieldValue.Of(new string('x', 1000)) };
        var original = Snapshot(a, protectedField);
        var current = Snapshot(a with { Value = FieldValue.Of(2) }, protectedField);
        var limit = original.Bytes;
        var change = new SemanticChange("A", "A", [], FieldValue.Of(1), FieldValue.Of(2), true, true, true)
        { CanonicalValueId = "A" };
        MemberContextResolution Resolve() => SemanticContextAdmission.Resolve(_ =>
        {
            if (cancel) cancellation.Cancel();
            return new Dictionary<string, SemanticMemberContext> { ["A"] = new("character", "character.dat") };
        }, [change], original, current, limit, cancellation.Token,
            new HashSet<string>(StringComparer.Ordinal) { "protected" });

        if (cancel) Assert.Throws<OperationCanceledException>(Resolve);
        else
        {
            var result = Resolve();
            Assert.False(result.Complete);
            Assert.Equal(0, result.AcceptedBytes);
            Assert.Equal(1, result.OmittedCount);
        }
        Assert.Equal(limit, original.Bytes);
        Assert.Equal(limit, current.Bytes);
        Assert.Equal(2, original.Fields.Count);
        Assert.Equal(2, current.Fields.Count);
        Assert.True(original.Complete);
        Assert.True(current.Complete);
        output.WriteLine($"ENTRY={limit}; protected original and current retained; context {(cancel ? "cancellation" : "insufficient capacity")} publishes no trimming or uncharged context.");
    }

    [Fact]
    public void PartialContextAndInsufficientCapacityCountEachMissingIdentityOnce()
    {
        var a = Field("A", canonical: true);
        var b = Field("B", canonical: true);
        var original = Snapshot(a, b);
        var current = Snapshot(a with { Value = FieldValue.Of(2) }, b with { Value = FieldValue.Of(2) });
        var changes = new[] { a, b }.Select(field => new SemanticChange(field.ValueId, field.Label, [],
            FieldValue.Of(1), FieldValue.Of(2), true, true, true) { CanonicalValueId = field.ValueId }).ToArray();

        var result = SemanticContextAdmission.Resolve(_ => new Dictionary<string, SemanticMemberContext>
        { ["A"] = new("character", "character.dat") }, changes, original, current, original.Bytes, CancellationToken.None);

        Assert.False(result.Complete);
        Assert.Empty(result.Contexts);
        Assert.Equal(2, result.OmittedCount);
        Assert.Equal(0, result.AcceptedBytes);
        Assert.Equal(2, original.Fields.Count);
        Assert.Equal(2, current.Fields.Count);
    }

    private static SemanticField Field(string id, bool canonical) => new(id, id, [], FieldValue.Of(1), false,
        CanonicalValueId: canonical ? id : null, Spec: IntegerSpec.Int32, MetadataPublished: canonical);

    private static SemanticSnapshot Snapshot(params SemanticField[] fields) => new(
        fields.ToDictionary(field => field.ValueId, StringComparer.Ordinal), true, fields.Sum(SemanticSnapshot.Estimate));

    private sealed class MetadataDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        public int GetterCalls { get; private set; }
        public Func<string, string[]> PathFor { get; set; } = _ => [];
        public Action<IReadOnlyList<string>>? OnResolve { get; init; }
        public bool FailGetter { get; init; }
        public bool Alias { get; init; }
        public override string Title => "Admission regression";
        public override ViewNode BuildView() => new TextNode("Targeted only");
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            OnResolve?.Invoke(valueIds);
            return valueIds.Select(id =>
            {
                var alias = Alias && id == "A";
                var metadata = new FieldMetadata(id, id, IntegerSpec.Int32 with { ReadOnly = alias })
                { Path = PathFor(id), CanonicalValueId = alias ? "P" : id,
                    BindingKind = alias ? FieldBindingKind.Derived : FieldBindingKind.Primary };
                RegisterField(metadata, () =>
                {
                    GetterCalls++;
                    if (FailGetter) throw new InvalidOperationException("getter failure");
                    return FieldValue.Of(1);
                }, alias ? null : _ => { });
                return metadata;
            }).ToArray();
        }
    }
}
