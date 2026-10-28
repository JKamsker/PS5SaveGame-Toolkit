using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroQueryTests
{
    private const string CollectionId = "astro.collectibles";
    private const string WorldAction = "collectibles/collect-world";
    private const string MemoryWorld = "World/MEMORY_HEAVEN";

    [Fact]
    public void WorldChoiceFallbackIncludesOnlyAttributedEligibleWorldsAndRejectsStageOrUnknownIdsThroughCore()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        var file = AstroTestData.Samples()[0];
        var document = plugin.Open(file, context);
        using var session = new DocumentSession("astro-picker", Assert.IsType<DiskSaveFile>(file), plugin, document, references: context);
        _ = session.BuildView();
        var action = Assert.Single(AstroCatalogueTestData.Catalogue(document).Children.OfType<ActionNode>(), action => action.ActionId == WorldAction);
        var parameter = Assert.Single(action.Parameters);
        var options = Assert.IsType<EnumSpec>(parameter.Spec).Options;
        Assert.Equal("Memory Meadow (Inferred)", Assert.Single(options, option => option.Value == MemoryWorld).Label);
        Assert.Contains("CC-BY-SA-3.0", parameter.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(options, option => option.Value == MemoryWorld + "/WEATHER");
        foreach (var id in new[] { MemoryWorld + "/WEATHER", "World/Unknown" })
        {
            var preview = session.PlanAction(WorldAction, new Dictionary<string, JsonElement> { ["world"] = JsonSerializer.SerializeToElement(id) });
            Assert.Null(preview.PlanId);
            Assert.NotEmpty(preview.Plan.Errors);
        }
        Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));
    }

    [Fact]
    public void QueryPagesSortFilterAndSearchTheWholeCatalogueWithDeterministicKeys()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        using var document = plugin.Open(AstroTestData.Samples()[0], context);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var keys = new List<string>();
        for (var offset = 0; offset < 164; offset += 17)
        {
            var page = queryable.Query(CollectionId, new() { Offset = offset, Limit = 17, SortColumn = "name" });
            Assert.Equal(164, page.TotalCount);
            Assert.Equal(164, page.FilteredCount);
            Assert.InRange(page.Rows.Count, 1, 17);
            keys.AddRange(page.Rows.Select(row => row.Key));
        }
        Assert.Equal(164, keys.Distinct(StringComparer.Ordinal).Count());
        var descending = queryable.Query(CollectionId, new() { Limit = 200, SortColumn = "name", Descending = true });
        Assert.Equal(keys.Order(StringComparer.Ordinal), descending.Rows.Select(row => row.Key).Order(StringComparer.Ordinal));
        var filtered = queryable.Query(CollectionId, new()
        {
            Text = "Puzzle/", Filters = new Dictionary<string, string> { ["world"] = "Memory Meadow", ["status"] = "Missing" }, Limit = 200
        });
        Assert.Equal(16, filtered.FilteredCount);
        Assert.All(filtered.Rows, row => Assert.StartsWith("Puzzle/", Assert.IsType<TextNode>(row.Cells["key"]).Text, StringComparison.Ordinal));
        Assert.Empty(queryable.Query(CollectionId, new() { Offset = int.MaxValue }).Rows);
        Assert.Single(queryable.Query(CollectionId, new() { Text = "Artifact/ps1_blackdisc" }).Rows);
        Assert.Equal(96, queryable.Query(CollectionId + ".Puzzle", new()).TotalCount);
        Assert.Equal(164, queryable.Query(CollectionId, new() { Text = "CC-BY-SA-3.0", SortColumn = "details" }).FilteredCount);
    }

    [Fact]
    public void InvalidQueriesAndUnresolvedScopesRejectWithoutMutation()
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes());
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        foreach (var query in new QueryableCollectionQuery[]
        {
            new() { Offset = -1 }, new() { Limit = 201 }, new() { Limit = 0 }, new() { Cursor = "garbage" },
            new() { Text = new string('x', 257) }, new() { SortColumn = "foreign" },
            new() { Filters = new Dictionary<string, string> { ["foreign"] = "x" } }
        }) Assert.Throws<ArgumentException>(() => queryable.Query(CollectionId, query));
        Assert.Throws<KeyNotFoundException>(() => queryable.Query("foreign", new()));
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        Assert.NotEmpty(planner.PlanAction("collectibles/collect-all", new Dictionary<string, FieldValue>(),
            new ActionScope { QueryScope = new(CollectionId, new(), 0) }).Errors);
        Assert.Equal(AstroTestData.Bytes(), AstroTestData.Output(document));
    }

    [Theory]
    [InlineData("collectibles/collect-all")]
    [InlineData(WorldAction)]
    public void CoreQuerySelectionsIncludeUnloadedRowsHonorExclusionsAndUndoAsOneGroup(string action)
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        var file = AstroTestData.Samples()[0];
        var document = plugin.Open(file, context);
        using var session = new DocumentSession("astro-query", Assert.IsType<DiskSaveFile>(file), plugin, document, references: context);
        _ = session.BuildView();
        var query = new QueryableCollectionQuery { Text = "Puzzle/", Filters = new Dictionary<string, string> { ["world"] = "Memory Meadow" }, Limit = 1 };
        var first = Assert.Single(session.QueryCollection(CollectionId, query).Rows);
        var parameters = action == WorldAction
            ? new Dictionary<string, JsonElement> { ["world"] = JsonSerializer.SerializeToElement(MemoryWorld) } : [];
        var scope = new ActionScope { QueryScope = new(CollectionId, query, session.State.Revision) { ExcludedRowKeys = [first.Key] } };
        var preview = session.PlanAction(action, parameters, scope, session.State.Revision);
        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Plan.Errors);
        Assert.Equal(15, preview.Scope.RowKeys!.Count);
        Assert.Equal(15, preview.Plan.Changes.Count);
        Assert.DoesNotContain(preview.Plan.Changes, change => change.ValueId.StartsWith(first.Key + "/", StringComparison.Ordinal));
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        var edited = AstroTestData.Output(document);
        using var reopened = plugin.Open(AstroTestData.File(edited), context);
        Assert.All(preview.Plan.Changes, change => Assert.Equal(change.NewValue, reopened.GetValue(change.ValueId)));
        Assert.Equal(FieldValue.Of(false), document.GetValue(first.Key + "/IsGot[0]/text[0]"));
        var refreshed = session.QueryCollection(CollectionId, query with { Limit = 200 });
        Assert.Equal(15, refreshed.Rows.Count(row => Assert.IsType<TextNode>(row.Cells["status"]).Text == "Owned"));
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, AstroTestData.Output(document));
    }

    [Fact]
    public void WorldPresetChangesOnlyExistingMappedOwnershipAndRejectsInvalidParametersAndStalePlans()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        foreach (var file in AstroTestData.Samples())
        {
            using var document = plugin.Open(file, context);
            var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
            var parameters = new Dictionary<string, FieldValue> { ["world"] = FieldValue.Of(MemoryWorld) };
            var plan = planner.PlanAction(WorldAction, parameters, new());
            Assert.Empty(plan.Errors);
            Assert.Equal(25, plan.Changes.Count); // nine artifacts + sixteen saved puzzle-prefix records
            Assert.Equal(139, plan.SkippedRows.Count);
            var expected = file.ReadAllBytes();
            var terminator = expected.AsSpan(AstroTestData.TextOffset).IndexOf((byte)0) + AstroTestData.TextOffset;
            var text = System.Text.Encoding.UTF8.GetString(expected, AstroTestData.TextOffset, terminator - AstroTestData.TextOffset);
            foreach (var change in plan.Changes)
            {
                var parts = change.ValueId.Split('/');
                var key = parts[3][..parts[3].LastIndexOf('[')];
                text = System.Text.RegularExpressions.Regex.Replace(text, $@"<{key}>.*?</{key}>", match =>
                    match.Value.Replace("<IsGot>false</IsGot>", "<IsGot>true</IsGot>", StringComparison.Ordinal),
                    System.Text.RegularExpressions.RegexOptions.Singleline);
            }
            Assert.True(planner.ApplyActionPlan(WorldAction, parameters, new(), plan).Succeeded);
            Assert.Equal(AstroTestData.Bytes(text), AstroTestData.Output(document));
            Assert.False(planner.ApplyActionPlan(WorldAction, parameters, new(), plan).Succeeded);
            using var reopened = plugin.Open(AstroTestData.File(AstroTestData.Output(document)), context);
            Assert.All(plan.Changes, change => Assert.Equal(change.NewValue, reopened.GetValue(change.ValueId)));
            foreach (var value in new[] { FieldValue.Of("World/UNKNOWN"), FieldValue.Of(true), FieldValue.Of(1L), FieldValue.Null })
                Assert.NotEmpty(planner.PlanAction(WorldAction, new Dictionary<string, FieldValue> { ["world"] = value }, new()).Errors);
            Assert.NotEmpty(planner.PlanAction(WorldAction, parameters, new ActionScope(["foreign"])).Errors);
            Assert.Empty(planner.PlanAction(WorldAction, parameters, new ActionScope([])).Changes);
        }
    }
}
