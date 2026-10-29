using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

[Collection(typeof(JsonPerformanceCollection))]
public sealed class JsonPerformanceTests(ITestOutputHelper output)
{
    private const int Entries = 50_000;
    private const long PageBudget = 100_000;
    // SDK-06 retains a shared immutable lexical tree alongside GenericJson's mutable structural
    // model. Keep this guard explicit for the reviewed dual-model adapter overhead.
    private const long OpeningBudget = 52_000_000;

    [Fact]
    public void OpeningAndFirstViewAvoidDuplicateNumberValidationAndHashBuffers()
    {
        var file = MemorySaveSet.SingleFile("large.json", Source());
        var plugin = new GenericJsonPlugin();
        var allocated = Allocated(OpenAndBuildView);
        output.WriteLine($"Opening allocations: {allocated}.");
        allocated.AssertAtMost(OpeningBudget);

        void OpenAndBuildView()
        {
            using var document = plugin.Open(file);
            _ = document.BuildView();
        }
    }

    [Fact]
    public void ProbeValidatesWithoutBuildingAnEditableTree()
    {
        var bytes = Source();
        var file = MemorySaveSet.SingleFile("large.json", bytes);
        var plugin = new GenericJsonPlugin();
        Assert.True(plugin.Probe(file).IsMatch);
        var allocated = Allocated(() => Assert.True(plugin.Probe(file).IsMatch));
        allocated.AssertInRange(0, bytes.Length + PageBudget);
    }

    [Fact]
    public void ExportReparsesWithoutMaterializingASecondTree()
    {
        var bytes = Source();
        using var document = Open(bytes);
        _ = document.Serialize();
        IReadOnlyList<FileOutput>? result = null;
        var allocated = Allocated(() => result = document.Serialize());
        allocated.AssertInRange(0, bytes.Length * 6L + PageBudget);
        Assert.Equal(bytes, result![0].Content.ToArray());
    }

    [Fact]
    public void RepeatedFirstViewReusesUnchangedStructureReview()
    {
        using var document = Open(Source());
        _ = document.BuildView();
        Allocated(() => document.BuildView()).AssertInRange(0, PageBudget);
    }

    [Theory]
    [InlineData("json-tree", false)]
    [InlineData("json-array-0", false)]
    [InlineData("json-array-0", true)]
    public void DeepPagesAllocateOnlyForReturnedRows(string collection, bool filtered)
    {
        using var document = Open(Source());
        var query = (IQueryableDocument)document;
        var request = new QueryableCollectionQuery { Offset = Entries - 20, Limit = 20,
            Filters = filtered ? new Dictionary<string, string> { ["state"] = "unchanged" } : new Dictionary<string, string>() };
        _ = query.Query(collection, request);
        QueryableCollectionPage? page = null;
        Allocated(() => page = query.Query(collection, request)).AssertInRange(0, PageBudget);
        Assert.Equal(20, page!.Rows.Count);
        Assert.Equal(collection == "json-tree" ? Entries + 1 : Entries, page.FilteredCount);
    }

    [Fact]
    public void MissingTextSearchAvoidsPerNodePathAndLabelStrings()
    {
        using var document = Open(Source());
        var query = (IQueryableDocument)document;
        var request = new QueryableCollectionQuery { Text = "missing-value" };
        Allocated(() => Assert.Empty(query.Query("json-array-0", request).Rows)).AssertInRange(0, PageBudget);
        Allocated(() => query.Query("json-array-0", request)).AssertInRange(0, PageBudget);
    }

    [Fact]
    public void SortingNarrowedMatchesAllocatesForMatchesAndPageOnly()
    {
        using var document = Open(Source());
        var query = (IQueryableDocument)document;
        var request = new QueryableCollectionQuery { Text = "499", SortColumn = "path", Descending = true, Limit = 20 };
        _ = query.Query("json-array-0", request);
        Allocated(() => query.Query("json-array-0", request)).AssertInRange(0, PageBudget);
        Assert.Throws<ArgumentException>(() => query.Query("json-array-0", new() { SortColumn = "path" }));
    }

    [Fact]
    public void SelectedResetResolvesOnlyRequestedIdentities()
    {
        using var document = Open(Source());
        Assert.True(document.ApplyEdits([new("value/50000", JsonEditingTests.Number("-0"))]).Succeeded);
        var planner = (IActionPlanningDocument)document;
        var scope = new ActionScope { RowKeys = ["value/50000"] };
        var parameters = new Dictionary<string, FieldValue>();
        _ = planner.PlanAction("reset-imported", parameters, scope);
        ActionPlan? plan = null;
        Allocated(() => plan = planner.PlanAction("reset-imported", parameters, scope)).AssertInRange(0, 10_000);
        Assert.Equal(JsonEditingTests.Number("49999"), Assert.Single(plan!.Changes).NewValue);
        Assert.Equal(JsonEditingTests.Number("-0"), document.GetValue("value/50000"));
    }

    private static ISaveDocument Open(byte[] source) => new GenericJsonPlugin().Open(MemorySaveSet.SingleFile("large.json", source));
    private static byte[] Source() => Encoding.UTF8.GetBytes("[" + string.Join(',', Enumerable.Range(0, Entries)) + "]");
    private static AllocationProbeResult Allocated(Action action) =>
        AllocationProbe.Measure(action, warmupRuns: 1);
}
