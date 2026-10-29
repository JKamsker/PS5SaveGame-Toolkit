using System.Text.Json;
using SaveToolkit.Core;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Core.Tests;

public sealed class EmptySlotTests : IDisposable
{
    private readonly QueryablePlugin _plugin = new(emptySlots: true);
    private readonly WorkspaceStore _store;

    public EmptySlotTests() => _store = new(new PluginCatalog([_plugin]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    private DocumentSession Open()
    {
        var zip = TestZips.Build(("PPSA99997/sdimg_SAVE/items.dat", QueryablePlugin.Create()));
        var workspace = _store.Create("empty slots", [new ImportSource("save.zip", () => new MemoryStream(zip))]);
        var session = workspace.Open(workspace.Set.AllDiskFiles.Single().Id, _plugin.Info.Id);
        _ = session.BuildView();
        return session;
    }

    [Fact]
    public void SdkMetadataRoundTrips_AndLegacyConstructorsKeepTheirDefaults()
    {
        var row = new TableRow("slot/127", new Dictionary<string, ViewNode> { ["name"] = new TextNode("0xFFFFFFFF") });
        var legacy = new TableNode([new("name", "Item")], [row]);
        Assert.False(row.IsEmpty);
        Assert.Null(legacy.EmptyRows);
        Assert.Null(new QueryableCollectionQuery().IncludeEmpty);
        var node = legacy with { EmptyRows = new("name"), Rows = [row with { IsEmpty = true }] };
        var json = JsonSerializer.Serialize<ViewNode>(node, ToolkitJson.Options);
        var restored = Assert.IsType<TableNode>(JsonSerializer.Deserialize<ViewNode>(json, ToolkitJson.Options));
        Assert.Equal(node.EmptyRows, restored.EmptyRows);
        Assert.True(restored.EmptyRows!.HideByDefault);
        Assert.Equal("empty slots", restored.EmptyRows.PluralLabel);
        Assert.True(restored.Rows.Single().IsEmpty);
        Assert.Equal("slot/127", restored.Rows.Single().Key);
        Assert.Equal("0xFFFFFFFF", Assert.IsType<TextNode>(restored.Rows.Single().Cells["name"]).Text);
    }

    [Fact]
    public void QueriesHideBeforePaging_AndCountOnlyMatchingEmptySlots()
    {
        using var session = Open();
        var query = new QueryableCollectionQuery { Text = "Item 050", Limit = 2, SortColumn = "id", Descending = true };
        var hidden = session.QueryCollection("items", query);
        Assert.False(_plugin.LastOpened!.LastQuery!.IncludeEmpty);
        Assert.Equal((100_000, 500, 500), (hidden.TotalCount, hidden.FilteredCount, hidden.HiddenEmptyCount));
        Assert.Equal(["50999", "50997"], hidden.Rows.Select(row => row.Key));
        Assert.All(hidden.Rows, row => Assert.False(row.IsEmpty));
        var next = session.QueryCollection("items", query with { Cursor = hidden.NextCursor });
        Assert.Equal(["50995", "50993"], next.Rows.Select(row => row.Key));
        var shown = session.QueryCollection("items", query with { IncludeEmpty = true });
        Assert.Equal((1000, 0), (shown.FilteredCount, shown.HiddenEmptyCount));
        Assert.Equal(["50999", "50998"], shown.Rows.Select(row => row.Key));
        Assert.True(shown.Rows[1].IsEmpty);
        _plugin.LastOpened.EmptyRows = new("name") { HideByDefault = false };
        Assert.Equal(1000, session.QueryCollection("items", query).FilteredCount);
        Assert.Equal(500, session.QueryCollection("items", query with { IncludeEmpty = false }).FilteredCount);
        Assert.False(session.State.IsDirty);
        Assert.Equal(QueryablePlugin.Create(), _plugin.LastOpened.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false, 499)]
    [InlineData(true, 999)]
    public void QueryScopesRetainVisibility_ExclusionsAndStableKeys_ThroughApplyUndo(bool includeEmpty, int count)
    {
        using var session = Open();
        var document = _plugin.LastOpened!;
        var query = new QueryableCollectionQuery { Text = "Item 050", IncludeEmpty = includeEmpty ? true : null };
        var scope = new ActionScope { QueryScope = new("items", query, session.State.Revision) { ExcludedRowKeys = ["50001"] } };
        var preview = session.PlanAction("increment", new Dictionary<string, JsonElement>(), scope);
        Assert.Equal(count, preview.Scope.RowKeys!.Count);
        Assert.Equal(includeEmpty, preview.Scope.QueryScope!.Query.IncludeEmpty);
        Assert.DoesNotContain("50001", preview.Scope.RowKeys);
        Assert.Equal(includeEmpty, preview.Scope.RowKeys.Contains("50000"));
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(50_004, document.GetValue("items/50003/value").AsInt64());
        Assert.True(session.Undo().Ok);
        Assert.Equal(50_003, document.GetValue("items/50003/value").AsInt64());
        Assert.False(session.State.IsDirty);
    }

    [Theory]
    [InlineData("negative")]
    [InlineData("too many")]
    [InlineData("empty returned")]
    [InlineData("hidden while shown")]
    public void InvalidEmptyRowPagesAreRejected(string problem)
    {
        using var session = Open();
        _plugin.LastOpened!.TransformPage = page => problem switch
        {
            "negative" => page with { HiddenEmptyCount = -1 },
            "too many" => page with { HiddenEmptyCount = page.TotalCount },
            "empty returned" => page with { Rows = [page.Rows[0] with { IsEmpty = true }] },
            _ => page with { HiddenEmptyCount = 1 },
        };
        Assert.Throws<InvalidOperationException>(() => session.QueryCollection("items",
            new QueryableCollectionQuery { Limit = 1, IncludeEmpty = problem == "hidden while shown" }));
    }

    [Fact]
    public void SaveDiffIncludesEmptySlots_RegardlessOfPresentationDefault()
    {
        var limits = new DecodeLimits { MaxQueryRows = 2, QueryPageSize = 2 };
        var tree = new PluginViewDecoder(_plugin).Decode(MemorySaveSet.SingleFile("items.dat", QueryablePlugin.Create()), limits);
        Assert.True(_plugin.LastOpened!.LastQuery!.IncludeEmpty);
        Assert.Contains(tree.Values, value => value.Path == "items/0/value");
        Assert.Contains(tree.Values, value => value.Path == "items/1/value");
    }
}
