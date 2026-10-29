using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class DocumentAllocationTests
{
    [Fact]
    public void LargestSampleOpenAndFirstViewStayWithinTheAllocationBudget()
    {
        const long allocationBudget = 24_000_000;
        var plugin = new InsomniacPlugin();
        var file = SampleSaves.Load("PPSA01474").AllDiskFiles
            .Where(file => plugin.Probe(file).IsMatch).MaxBy(file => file.Length)!;
        Assert.Equal(577_928, file.Length);
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474");

        var allocated = AllocationProbe.MeasureOpen(() => plugin.Open(file, references), out var opened, document => _ = document.BuildView());
        using var document = opened;
        var view = document.BuildView();

        allocated.AssertAtMost(allocationBudget, $"Open + first view allocated {allocated.MinimumBytes:N0} bytes; budget {allocationBudget:N0}.");
        GC.KeepAlive(view);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void RepeatedBalancePlansStaySmallAndDoNotChangeTheDocument()
    {
        const int repetitions = 100;
        const long bytesPerPlanBudget = 4_096;
        using var document = new InsomniacPlugin().Open(PlayerCorpusTests.Find("i29slot0-s-autosave", 0));
        _ = document.BuildView();
        var parameters = new Dictionary<string, FieldValue> { [RiftBoltPlanning.TargetParameter] = FieldValue.Of(1_000L) };
        var scope = new ActionScope();
        var planning = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var expected = planning.PlanAction(RiftBoltPlanning.ActionId, parameters, scope);
        var original = document.GetValue(RiftScalarWrites.CareerId);

        ActionPlan? plan = null;
        var allocated = AllocationProbe.Measure(() =>
        {
            for (var index = 0; index < repetitions; index++)
                plan = planning.PlanAction(RiftBoltPlanning.ActionId, parameters, scope);
        });

        allocated.AssertAtMost(repetitions * bytesPerPlanBudget, $"Plans allocated {allocated.MinimumBytes:N0} bytes.");
        Assert.NotNull(plan);
        Assert.Empty(plan.Errors);
        Assert.Equal(expected.Changes, plan.Changes);
        Assert.Equal([RiftScalarWrites.CareerId, RiftScalarWrites.BalanceId], plan.Changes.Select(change => change.ValueId));
        Assert.Equal(original, document.GetValue(RiftScalarWrites.CareerId));
    }
}
