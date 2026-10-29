using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class JsonSemanticPerformanceTests
{
    [Fact]
    public void RepeatedSampleFirstViewReusesLiveTitleAdapters()
    {
        var file = SampleSaves.Load("PPSA04027").AllDiskFiles.First(file => file.FileName == "settings.json");
        using var document = new GenericJsonPlugin().Open(file);
        _ = document.BuildView();
        var allocated = AllocationProbe.Measure(() =>
        {
            _ = document.BuildView();
        });
        allocated.AssertAtMost(100_000);
    }

    [Fact]
    public void UnchangedSelectedWrapperPreviewDoesNotRebuildTargetsOrCloneTheTree()
    {
        var source = Wrapper(50_000);
        using var document = new GenericJsonPlugin().Open(MemorySaveSet.SingleFile("imgui_emu_option.json", Encoding.UTF8.GetBytes(source), "PPSA07696"));
        var planner = (IActionPlanningDocument)document;
        var parameters = new Dictionary<string, FieldValue>();
        var scope = new ActionScope { RowKeys = ["value/2"] };
        _ = planner.PlanAction("semantic/wrapper-imported", parameters, scope);
        var allocated = AllocationProbe.Measure(() => planner.PlanAction("semantic/wrapper-imported", parameters, scope), out var plan);
        allocated.AssertAtMost(100_000);
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.NotEmpty(plan.Warnings);
    }

    [Fact]
    public void DuplicatePastAdapterLimitStillMakesFirstOccurrenceIneligible()
    {
        var source = Wrapper(5_001).Replace("},\"settings\"", ",\"b0\":false},\"settings\"", StringComparison.Ordinal);
        using var document = new GenericJsonPlugin().Open(MemorySaveSet.SingleFile("imgui_emu_option.json", Encoding.UTF8.GetBytes(source), "PPSA07696"));
        var planner = (IActionPlanningDocument)document;
        var plan = planner.PlanAction("semantic/wrapper-imported", new Dictionary<string, FieldValue>(), new() { RowKeys = ["value/2"] });
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Single(plan.SkippedRows);
        Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
    }

    private static string Wrapper(int count) => "{\"options\":{" + string.Join(',', Enumerable.Range(0, count).Select(i => $"\"b{i}\":true")) + "},\"settings\":{}}";
}
