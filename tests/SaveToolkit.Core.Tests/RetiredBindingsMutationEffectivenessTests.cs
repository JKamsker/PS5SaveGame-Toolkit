using System.Text.Json;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class RetiredBindingsMutationEffectivenessTests
{
    [Fact]
    public void ExactlyOneHundredThousandMissingLazyIdsRemainRememberedForTheirReturn()
    {
        const int limit = 100_000;
        var document = new RememberedLazyDocument(limit);
        using var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();
        for (var i = 0; i < limit; i++) fixture.Session.Expand($"lazy/{i}");
        var parameters = new Dictionary<string, JsonElement>();
        Assert.True(fixture.Session.InvokeAction("toggle", parameters).Ok);
        Assert.True(fixture.Session.InvokeAction("toggle", parameters).Ok);
        Assert.Equal(2 * limit, document.Expansions);
    }

    [Fact]
    public void EmptyLazyExpansionsHaveLinearAllocationGrowth()
    {
        var smaller = MeasureEmptyExpansionAllocations(4_000);
        var larger = MeasureEmptyExpansionAllocations(8_000);

        // The expansion work itself is linear in the request count. A copied index would add
        // every previously remembered id on each request and exceed this deliberately generous bound.
        Assert.InRange(larger, 0, checked(smaller * 2 + 1_000_000));
    }

    private static long MeasureEmptyExpansionAllocations(int count)
    {
        using var fixture = new MutationSessionFixture(new RememberedLazyDocument(count));
        fixture.Session.BuildView();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < count; i++) fixture.Session.Expand($"lazy/{i}");
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void RetiredIdsAreRetainedAtTheExactCapAndDiscardedOnlyBeyondIt()
    {
        using var fixture = new MutationSessionFixture(new RetiringDocument());
        fixture.Session.BuildView();
        var parameters = new Dictionary<string, JsonElement>();
        Assert.True(fixture.Session.InvokeAction("next", parameters).Ok);
        Assert.True(fixture.Session.InvokeAction("next", parameters).Ok);
        Assert.True(fixture.Session.ApplyEdits([new("phase0/0", JsonSerializer.SerializeToElement(2))]).Stale);
        Assert.True(fixture.Session.InvokeAction("next", parameters).Ok);
        var unknown = fixture.Session.ApplyEdits([new("phase0/0", JsonSerializer.SerializeToElement(2))]);
        Assert.False(unknown.Ok);
        Assert.False(unknown.Stale);
        Assert.True(fixture.Session.ApplyEdits([new("phase2/0", JsonSerializer.SerializeToElement(2))]).Stale);
    }

    private sealed class RetiringDocument : BoundSaveDocument
    {
        private int _phase;
        public override string Title => "Retiring bindings";
        public override ViewNode BuildView()
        {
            const int retirementLimit = 100_000;
            var count = _phase == 1 ? retirementLimit - 1 : 1;
            ViewNode[] fields = Enumerable.Range(0, count).Select(i => (ViewNode)new FieldNode(
                $"phase{_phase}/{i}", "Value", IntegerSpec.UInt8, FieldValue.Of(1L))).ToArray();
            return new SectionNode(Title, [new ActionNode("next", "Next"), .. fields]);
        }
        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
        { _phase++; return new() { StructureChanged = true }; }
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }

    private sealed class RememberedLazyDocument(int count) : BoundSaveDocument
    {
        private bool _visible = true;
        public int Expansions { get; private set; }
        public override string Title => "Remembered sections";
        public override ViewNode BuildView() => new SectionNode(Title,
            [new ActionNode("toggle", "Toggle"), .. (_visible ? Enumerable.Range(0, count)
                .Select(i => (ViewNode)SectionNode.Lazy("Lazy", $"lazy/{i}")) : [])]);
        public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) { Expansions++; return []; }
        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
        { _visible = !_visible; return new() { StructureChanged = true }; }
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
