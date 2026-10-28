using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Core.Tests;

public sealed class QueryActionPlanningTests : IDisposable
{
    private readonly QueryablePlugin _plugin = new();
    private readonly WorkspaceStore _store;

    public QueryActionPlanningTests() => _store = new WorkspaceStore(new PluginCatalog([_plugin]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    private DocumentSession Open(int maxRows = 5_000)
    {
        var zip = TestZips.Build(("PPSA99997/sdimg_SAVE/items.dat", QueryablePlugin.Create()));
        var workspace = _store.Create("query planning", [new ImportSource("save.zip", () => new MemoryStream(zip))]);
        var file = workspace.Set.AllDiskFiles.Single();
        var session = new DocumentSession("fixture", file, _plugin, _plugin.Open(file),
            new DocumentSessionOptions { MaxQueryActionRows = maxRows }, () => new QueryablePlugin().Open(file));
        _ = session.BuildView();
        return session;
    }

    private static ActionScope Scope(long revision = 0) => new()
    {
        QueryScope = new ActionQueryScope("items", new QueryableCollectionQuery
        {
            Text = "Item",
            Filters = new Dictionary<string, string> { ["name"] = "Item 050" },
            SortColumn = "id",
            Descending = true,
        }, revision),
    };

    private static long Value(DocumentSession session, string key) => ((SaveToolkit.Sdk.Views.FieldNode)session.QueryCollection("items",
        new QueryableCollectionQuery { Text = $"Item {int.Parse(key, System.Globalization.CultureInfo.InvariantCulture):D6}", Limit = 1 })
        .Rows.Single().Cells["value"]).Value.AsInt64();

    [Theory]
    [InlineData("offset")]
    [InlineData("cursor")]
    [InlineData("cursor without offsets")]
    public void FilteredSelection_ResolvesUnloadedPagesAndExclusions_AndAppliesAsOneUndoGroup(string paging)
    {
        using var session = Open(maxRows: 1_000);
        _plugin.LastOpened!.TransformPage = page => paging switch
        {
            "offset" => page with { NextCursor = null },
            "cursor without offsets" => page with { Offset = 0 },
            _ => page,
        };
        var scope = Scope();
        scope = scope with { QueryScope = scope.QueryScope! with { ExcludedRowKeys = ["50999", "50000"] } };
        var preview = session.PlanAction("increment", new Dictionary<string, JsonElement>(), scope);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(5, _plugin.LastOpened!.QueryCount);
        Assert.Equal(998, preview.Scope.RowKeys!.Count);
        Assert.Equal("50998", preview.Scope.RowKeys[0]);
        Assert.Equal("50001", preview.Scope.RowKeys[^1]);
        Assert.Equal(preview.Scope, _plugin.LastOpened.LastPlannedScope);
        Assert.Equal("items", _plugin.LastOpened.LastPlannedScope!.QueryScope!.CollectionId);
        Assert.Equal(998, preview.Plan.Changes.Count);
        Assert.Equal(0, _plugin.LastOpened.ApplyCount);
        Assert.False(session.State.IsDirty);
        Assert.Equal(50_001, Value(session, "50001"));

        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        Assert.Equal(1, _plugin.LastOpened.ApplyCount);
        Assert.Equal(50_002, Value(session, "50001"));
        Assert.Equal(50_000, Value(session, "50000"));
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.False(session.State.IsDirty);
        Assert.Equal(50_001, Value(session, "50001"));
        Assert.True(session.Redo().Ok);
        Assert.Equal(50_002, Value(session, "50001"));
    }

    [Fact]
    public void RowBound_RejectsBeforeWalkingWholeCollection_AndCannotBeBypassedWithExclusions()
    {
        using var session = Open(maxRows: 999);
        var scope = Scope();
        scope = scope with { QueryScope = scope.QueryScope! with { ExcludedRowKeys = ["50000"] } };
        var preview = session.PlanAction("increment", new Dictionary<string, JsonElement>(), scope);
        Assert.Null(preview.PlanId);
        Assert.Contains(preview.Plan.Errors, error => error.Message.Contains("999 matching rows", StringComparison.Ordinal));
        Assert.Equal(1, _plugin.LastOpened!.QueryCount);
        Assert.Null(_plugin.LastOpened.LastPlannedScope);
        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public void StaleQueryScope_IsRejectedEvenWithCurrentRequestRevision_AndMutationInvalidatesApply()
    {
        using var session = Open();
        var preview = session.PlanAction("increment", new Dictionary<string, JsonElement>(), Scope());
        Assert.True(session.ApplyEdits([new IncomingEdit("items/50001/value", JsonSerializer.SerializeToElement("4"))]).Ok);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Stale);
        var queries = _plugin.LastOpened!.QueryCount;
        var stale = session.PlanAction("increment", new Dictionary<string, JsonElement>(), Scope(), session.State.Revision);
        Assert.True(stale.Stale);
        Assert.Null(stale.PlanId);
        Assert.Equal(queries, _plugin.LastOpened.QueryCount);
        Assert.Equal(0, _plugin.LastOpened.ApplyCount);
    }

    [Fact]
    public void QueryAndExclusions_AreCopiedAndPaginationIsReset()
    {
        using var session = Open();
        var filters = new Dictionary<string, string> { ["name"] = "Item 050" };
        string[] excluded = ["50000"];
        var scope = new ActionScope { QueryScope = new ActionQueryScope("items", new QueryableCollectionQuery
        {
            Filters = filters, Offset = 900, Cursor = "900", Limit = 1,
        }, 0) { ExcludedRowKeys = excluded } };
        var preview = session.PlanAction("increment", new Dictionary<string, JsonElement>(), scope);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(999, preview.Scope.RowKeys!.Count);
        Assert.Equal(0, preview.Scope.QueryScope!.Query.Offset);
        Assert.Null(preview.Scope.QueryScope.Query.Cursor);
        filters["name"] = "Item 060";
        excluded[0] = "50001";
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        Assert.Equal(50_000, Value(session, "50000"));
        Assert.Equal(50_002, Value(session, "50001"));
        Assert.Equal(60_001, Value(session, "60001"));
    }

    [Fact]
    public void ZeroMatches_AndFullyExcludedSelection_AreEmptyNeverWholeDocument()
    {
        using var session = Open();
        foreach (var text in new[] { "no matching items", "Item 050000" })
        {
            var scope = new ActionScope { QueryScope = new ActionQueryScope("items",
                new QueryableCollectionQuery { Text = text }, session.State.Revision) { ExcludedRowKeys = ["50000"] } };
            var preview = session.PlanAction("increment", new Dictionary<string, JsonElement>(), scope);
            Assert.NotNull(preview.PlanId);
            Assert.Empty(preview.Scope.RowKeys!);
            Assert.Empty(preview.Plan.Changes);
            Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
            Assert.False(session.State.IsDirty);
        }
    }

    [Theory]
    [InlineData("both")]
    [InlineData("duplicate exclusions")]
    [InlineData("empty exclusion")]
    [InlineData("too many exclusions")]
    [InlineData("unknown collection")]
    [InlineData("unknown filter")]
    [InlineData("unknown sort")]
    [InlineData("long text")]
    public void InvalidScopes_AreRejectedWithoutCallingPlanner(string invalid)
    {
        using var session = Open();
        var scope = Scope();
        var query = scope.QueryScope!;
        scope = invalid switch
        {
            "both" => scope with { RowKeys = [] },
            "duplicate exclusions" => scope with { QueryScope = query with { ExcludedRowKeys = ["a", "a"] } },
            "empty exclusion" => scope with { QueryScope = query with { ExcludedRowKeys = [""] } },
            "too many exclusions" => scope with { QueryScope = query with { ExcludedRowKeys = Enumerable.Range(0, 5001).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray() } },
            "unknown collection" => scope with { QueryScope = query with { CollectionId = "missing" } },
            "unknown filter" => scope with { QueryScope = query with { Query = query.Query with { Filters = new Dictionary<string, string> { ["missing"] = "x" } } } },
            "unknown sort" => scope with { QueryScope = query with { Query = query.Query with { SortColumn = "missing" } } },
            _ => scope with { QueryScope = query with { Query = query.Query with { Text = new string('x', QueryableCollectionLimits.MaxQueryLength + 1) } } },
        };
        var preview = session.PlanAction("increment", new Dictionary<string, JsonElement>(), scope);
        Assert.Null(preview.PlanId);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.Null(_plugin.LastOpened!.LastPlannedScope);
        Assert.False(session.State.IsDirty);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("empty")]
    [InlineData("count")]
    [InlineData("offset")]
    [InlineData("extra cursor")]
    [InlineData("long cursor")]
    public void MalformedPaging_IsRejectedWithoutCallingPlanner(string invalid)
    {
        using var session = Open();
        _plugin.LastOpened!.TransformPage = page => invalid switch
        {
            "duplicate" when page.Offset > 0 => page with { Rows = [page.Rows[0] with { Key = "50999" }, .. page.Rows.Skip(1)] },
            "empty" when page.Offset > 0 => page with { Rows = [] },
            "count" when page.Offset > 0 => page with { FilteredCount = page.FilteredCount - 1 },
            "offset" => page with { Offset = 0, NextCursor = null },
            "extra cursor" => page with { FilteredCount = page.Rows.Count, NextCursor = "200" },
            "long cursor" => page with { NextCursor = new string('x', QueryableCollectionLimits.MaxCursorLength + 1) },
            _ => page,
        };
        var preview = session.PlanAction("increment", new Dictionary<string, JsonElement>(), Scope());
        Assert.Null(preview.PlanId);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.Null(_plugin.LastOpened.LastPlannedScope);
        Assert.InRange(_plugin.LastOpened.QueryCount, 1, 2);
    }

    [Fact]
    public void Apply_ReResolvesKeysEvenIfPluginEffectsAreUnchanged()
    {
        using var session = Open();
        var preview = session.PlanAction("increment", new Dictionary<string, JsonElement>(), Scope());
        _plugin.LastOpened!.TransformPage = page => page with { Rows = page.Rows.Reverse().ToArray() };
        var result = session.ApplyActionPlan(preview.PlanId!, preview.Revision);
        Assert.True(result.Stale);
        Assert.Contains(result.Errors, error => error.Message.Contains("query selection changed", StringComparison.Ordinal));
        Assert.Equal(0, _plugin.LastOpened.ApplyCount);
        Assert.False(session.State.IsDirty);
    }
}
