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
    private const long OpeningBudget = 44_000_000;
    private const int MeasuredOpeningRuns = 3;

    [Fact]
    public void OpeningAndFirstViewAvoidDuplicateNumberValidationAndHashBuffers()
    {
        var file = MemorySaveSet.SingleFile("large.json", Source());
        var plugin = new GenericJsonPlugin();
        OpenAndBuildView();
        // Gate steady-state allocations without letting one noisy run decide the result.
        var measurements = new long[MeasuredOpeningRuns];
        for (var run = 0; run < measurements.Length; run++)
            measurements[run] = Allocated(OpenAndBuildView);
        var allocated = measurements.Min();
        output.WriteLine($"Opening allocations (bytes): {string.Join(", ", measurements)}; minimum: {allocated}.");
        Assert.InRange(allocated, 0, OpeningBudget);

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
        Assert.InRange(allocated, 0, bytes.Length + PageBudget);
    }

    [Fact]
    public void ExportReparsesWithoutMaterializingASecondTree()
    {
        var bytes = Source();
        using var document = Open(bytes);
        _ = document.Serialize();
        IReadOnlyList<FileOutput>? result = null;
        var allocated = Allocated(() => result = document.Serialize());
        Assert.InRange(allocated, 0, bytes.Length * 6L + PageBudget);
        Assert.Equal(bytes, result![0].Content.ToArray());
    }

    [Fact]
    public void RepeatedFirstViewReusesUnchangedStructureReview()
    {
        using var document = Open(Source());
        _ = document.BuildView();
        Assert.InRange(Allocated(() => document.BuildView()), 0, PageBudget);
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
        Assert.InRange(Allocated(() => page = query.Query(collection, request)), 0, PageBudget);
        Assert.Equal(20, page!.Rows.Count);
        Assert.Equal(collection == "json-tree" ? Entries + 1 : Entries, page.FilteredCount);
    }

    [Fact]
    public void MissingTextSearchAvoidsPerNodePathAndLabelStrings()
    {
        using var document = Open(Source());
        var query = (IQueryableDocument)document;
        var request = new QueryableCollectionQuery { Text = "missing-value" };
        Assert.InRange(Allocated(() => Assert.Empty(query.Query("json-array-0", request).Rows)), 0, PageBudget);
        Assert.InRange(Allocated(() => query.Query("json-array-0", request)), 0, PageBudget);
    }

    [Fact]
    public void SortingNarrowedMatchesAllocatesForMatchesAndPageOnly()
    {
        using var document = Open(Source());
        var query = (IQueryableDocument)document;
        var request = new QueryableCollectionQuery { Text = "499", SortColumn = "path", Descending = true, Limit = 20 };
        _ = query.Query("json-array-0", request);
        Assert.InRange(Allocated(() => query.Query("json-array-0", request)), 0, PageBudget);
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
        Assert.InRange(Allocated(() => plan = planner.PlanAction("reset-imported", parameters, scope)), 0, 10_000);
        Assert.Equal(JsonEditingTests.Number("49999"), Assert.Single(plan!.Changes).NewValue);
        Assert.Equal(JsonEditingTests.Number("-0"), document.GetValue("value/50000"));
    }

    private static ISaveDocument Open(byte[] source) => new GenericJsonPlugin().Open(MemorySaveSet.SingleFile("large.json", source));
    private static byte[] Source() => Encoding.UTF8.GetBytes("[" + string.Join(',', Enumerable.Range(0, Entries)) + "]");
    private static long Allocated(Action action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
