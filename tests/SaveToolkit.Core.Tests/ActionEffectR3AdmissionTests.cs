using System.Collections;
using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Tests.SdkHelpers;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class ActionEffectR3AdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectSnapshotAndSerializedSizeStopHonestAggregateBeforeNextRowOrCopies(bool measure)
    {
        var graph = new CountedGraph();
        var rows = new VisitedList<PlannedChange>([Change(graph.Spec), Change(IntegerSpec.Int32 with { ReadOnly = true }) with { ValueId = "next" }]);
        var plan = new ActionPlan { Changes = rows };
        Assert.Throws<InvalidOperationException>(() =>
        {
            if (measure) _ = ActionPlanSnapshots.SerializedSize(plan);
            else _ = ActionPlanSnapshots.Snapshot(plan);
        });
        Assert.Equal(1, rows.Visits);
        Assert.InRange(graph.EnumVisits, 1, 250_000); // Full truthful graph would visit 16,773,120 entries.
        Assert.InRange(graph.BranchVisits, 1, 60);
        Assert.Equal(0, graph.IndexReads); // No detached option array copy or indexed fallback.
    }

    [Fact]
    public void ActualSessionStopsHonestAggregateBeforeResolverMementoOrWriter()
    {
        using var fixture = new ActionEffectR2Fixture();
        var initialResolverCalls = fixture.Document.ResolverCalls;
        var graph = new CountedGraph();
        fixture.Document.Preview = () => new ActionPlan { Changes = [Change(graph.Spec)] };

        var preview = fixture.Session.PlanAction("change", new Dictionary<string, JsonElement>());

        Assert.Null(preview.PlanId);
        Assert.Contains(preview.Plan.Errors, error => error.Message.Contains("large", StringComparison.Ordinal));
        Assert.InRange(graph.EnumVisits, 1, 250_000);
        Assert.Equal(initialResolverCalls, fixture.Document.ResolverCalls);
        Assert.Equal(0, fixture.Document.CaptureCalls);
        Assert.Equal(0, fixture.Document.WriterCalls);
    }

    [Fact]
    public void NearlyExhaustedCanonicalAllowanceRejectsDeclaredGraphBeforeAnyNestedVisitOrGetter()
    {
        var graph = new CountedGraph();
        using var document = new CanonicalGraphDocument(graph.Spec);
        var index = new ViewIndex(document);
        var plan = new ActionPlan { Changes = [new("canonical", "Canonical", FieldValue.Of(1), FieldValue.Of(2))] };

        Assert.Throws<InvalidOperationException>(() => FieldMetadataPlanning.Validate(index, document, plan,
            null, CancellationToken.None, metadataBudget: 1000));

        Assert.Equal(1, document.ResolverCalls);
        Assert.Equal(0, document.GetterCalls);
        Assert.Equal(0, graph.BranchVisits);
        Assert.Equal(0, graph.EnumVisits);
        Assert.Null(index.ResolvedMetadata("canonical"));
    }

    [Fact]
    public void AdapterRejectsAggregateBeforeInvokingAnyPathCodec()
    {
        var graph = new CountedGraph();
        using var document = new MemberAdapterDocument();
        var codecCalls = 0;
        var adapter = new DocumentMemberAdapter("member", document, new DocumentMemberIdentityPolicy
        {
            NavigationPath = (_, path) => { codecCalls++; return path; },
        });
        var plan = new ActionPlan { Changes = [Change(graph.Spec) with
        { Effect = new(ActionEffectKind.Observation, graph.Spec) { Path = ["local"] } }] };
        Assert.Throws<InvalidOperationException>(() => adapter.ToGroupPlan(plan));
        Assert.Equal(0, codecCalls);
        Assert.InRange(graph.EnumVisits, 1, 250_000);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void DirectSdkPreservesExactInclusiveUtf8PlanBytesAndRejectsOneExtra(int excess, bool accepted)
    {
        var plan = new ActionPlan { Warnings = [""] };
        var overhead = JsonSerializer.SerializeToUtf8Bytes(plan).Length;
        // Non-ASCII is escaped by the default wire serializer. The lower bound must not
        // substitute UTF-16 storage as the final inclusive wire admission unit.
        var unicode = "é";
        var unicodeBytes = JsonSerializer.SerializeToUtf8Bytes(unicode).Length - 2;
        plan = plan with { Warnings = [unicode + new string('x', ActionPlanSnapshots.MaxSerializedBytes - overhead - unicodeBytes + excess)] };
        if (accepted)
        {
            Assert.Equal(ActionPlanSnapshots.MaxSerializedBytes, ActionPlanSnapshots.SerializedSize(plan));
            Assert.Single(ActionPlanSnapshots.Snapshot(plan).Warnings);
        }
        else Assert.Throws<InvalidOperationException>(() => ActionPlanSnapshots.Snapshot(plan));
    }

    private static PlannedChange Change(FieldSpec spec) => new("effect", "Effect", FieldValue.Of(1), FieldValue.Of(2))
    { Effect = new(ActionEffectKind.Observation, spec) };

    private sealed class CountedGraph
    {
        private readonly VisitedList<EnumOption> _enum;
        private readonly VisitedList<UnionOption> _branches;
        public CountedGraph()
        {
            _enum = new(Enumerable.Range(0, 4096).Select(_ => new EnumOption("", "")).ToArray());
            var choice = new EnumSpec(_enum);
            _branches = new(Enumerable.Range(0, 4095).Select(_ => new UnionOption("", "", choice)).ToArray());
            Spec = new UnionSpec(_branches) { ReadOnly = true };
        }
        public FieldSpec Spec { get; }
        public int EnumVisits => _enum.Visits;
        public int BranchVisits => _branches.Visits;
        public int IndexReads => _enum.IndexReads + _branches.IndexReads;
    }

    private sealed class CanonicalGraphDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private readonly FieldSpec _spec;
        public CanonicalGraphDocument(FieldSpec spec)
        {
            _spec = spec;
            RegisterField("canonical", IntegerSpec.Int32, () => { GetterCalls++; return FieldValue.Of(1); });
        }
        public int ResolverCalls { get; private set; }
        public int GetterCalls { get; private set; }
        public override string Title => "Canonical graph";
        public override ViewNode BuildView() => new TextNode(Title);
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> ids)
        { ResolverCalls++; return [new("canonical", "Canonical", _spec)]; }
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}

internal sealed class VisitedList<T>(IReadOnlyList<T> values) : IReadOnlyList<T>
{
    public int Visits { get; private set; }
    public int IndexReads { get; private set; }
    public int Count => values.Count;
    public T this[int index] { get { IndexReads++; return values[index]; } }
    public IEnumerator<T> GetEnumerator()
    { foreach (var value in values) { Visits++; yield return value; } }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
