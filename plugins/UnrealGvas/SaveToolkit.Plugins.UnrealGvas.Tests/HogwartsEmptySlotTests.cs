using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsEmptySlotTests
{
    [Fact]
    public void InventoryUsesOneTablePerImageWithMatchingEmptyPresentationAndUnchangedBytes()
    {
        var file = HogwartsInventoryFixture.Create();
        using var document = new UnrealGvasPlugin().Open(file);
        var tabs = Assert.IsType<TabsNode>(document.BuildView()).Tabs;
        var tables = tabs.Single(tab => tab.Title == "Inventory").Children.OfType<QueryableTableNode>().ToArray();
        var inventories = tables.Where(table => table.CollectionId.EndsWith("/InventoryDynamic", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, inventories.Length);
        var collections = Assert.IsAssignableFrom<IQueryableDocument>(document).Collections;
        Assert.All(inventories, table =>
        {
            Assert.Equal(HogwartsInventoryFixture.TotalSlots, table.TotalCount);
            Assert.Equal(new EmptyRowPresentation("ItemID"), table.EmptyRows);
            Assert.Equal(table.EmptyRows, collections.Single(collection => collection.Id == table.CollectionId).EmptyRows);
        });
        Assert.All(tables.Except(inventories), table => Assert.Null(table.EmptyRows));
        Assert.DoesNotContain(tabs.SelectMany(tab => tab.Children).OfType<SectionNode>(), section => section.Title.StartsWith("Slots ", StringComparison.Ordinal));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void VisibilityIsAppliedBeforePagingAndRetainsStableSlotKeys(bool? includeEmpty)
    {
        using var document = new UnrealGvasPlugin().Open(HogwartsInventoryFixture.Create());
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var query = new QueryableCollectionQuery { IncludeEmpty = includeEmpty, SortColumn = "SlotNumber", Descending = true, Limit = 37 };
        var rows = ReadAll(queryable, query);
        bool shown = includeEmpty == true;
        Assert.Equal(shown ? 410 : 207, rows.Count);
        Assert.Equal(rows.Count, rows.Select(row => row.Key).Distinct().Count());
        Assert.Equal(shown ? 203 : 0, rows.Count(row => row.IsEmpty));
        Assert.Equal(shown ? Enumerable.Range(0, 410).Reverse() : Enumerable.Range(0, 410).Reverse().Where(slot => slot % 2 == 1 || slot >= 405),
            rows.Select(row => int.Parse(Assert.IsType<TextNode>(row.Cells["SlotNumber"]).Text)));
        var single = queryable.Query(HogwartsInventoryFixture.Collection, new() { Filters = new Dictionary<string, string> { ["SlotNumber"] = "201" } });
        Assert.Equal(rows.Single(row => Assert.IsType<TextNode>(row.Cells["SlotNumber"]).Text == "201").Key, Assert.Single(single.Rows).Key);
        Assert.All(rows.Where(row => row.IsEmpty), row => Assert.Equal("NULL", Assert.IsType<TextNode>(row.Cells["ItemID"]).Text));
    }

    [Fact]
    public void SearchAndFiltersCountOnlyMatchingHiddenRowsAndKeepAmbiguousRecordsVisible()
    {
        using var document = new UnrealGvasPlugin().Open(HogwartsInventoryFixture.Create());
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var query = new QueryableCollectionQuery { Text = "bAg", Filters = new Dictionary<string, string> { ["Count"] = "0" } };
        var hidden = queryable.Query(HogwartsInventoryFixture.Collection, query);
        Assert.Equal((3, 203), (hidden.FilteredCount, hidden.HiddenEmptyCount));
        Assert.Equal(3, hidden.Rows.Count);
        var shown = queryable.Query(HogwartsInventoryFixture.Collection, query with { IncludeEmpty = true });
        Assert.Equal((206, 0), (shown.FilteredCount, shown.HiddenEmptyCount));
        var named = queryable.Query(HogwartsInventoryFixture.Collection, new() { Text = "UnmappedZero" });
        Assert.Equal((1, 0), (named.FilteredCount, named.HiddenEmptyCount));
        Assert.False(Assert.Single(named.Rows).IsEmpty);
        var absent = queryable.Query(HogwartsInventoryFixture.Collection, new() { Text = "not-present" });
        Assert.Equal((0, 0), (absent.FilteredCount, absent.HiddenEmptyCount));
        Assert.Empty(absent.Rows);
        var equipped = queryable.Query("hogwarts/RawDatabaseImage/EquippedGearDynamic", new());
        Assert.False(Assert.Single(equipped.Rows).IsEmpty);
    }

    [Fact]
    public void VisibleSlotEditReopensAtSameIdentityAndLeavesEveryOtherSlotUnchanged()
    {
        var file = HogwartsInventoryFixture.Create();
        var plugin = new UnrealGvasPlugin();
        using var document = plugin.Open(file);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var before = ReadAll(queryable, new() { IncludeEmpty = true });
        var row = before.Single(row => Assert.IsType<TextNode>(row.Cells["SlotNumber"]).Text == "201");
        var field = Assert.IsType<FieldNode>(row.Cells["Count"]);
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(999L))]).Succeeded);
        using var reopened = plugin.Open(MemorySaveSet.SingleFile(file.RelativePath, Assert.Single(document.Serialize()).Content.ToArray(), "PPSA01603"));
        var after = ReadAll(Assert.IsAssignableFrom<IQueryableDocument>(reopened), new() { IncludeEmpty = true });
        Assert.Equal(before.Select(item => item.Key), after.Select(item => item.Key));
        foreach (var original in before)
        {
            var current = after.Single(item => item.Key == original.Key);
            Assert.Equal(original.IsEmpty, current.IsEmpty);
            Assert.Equal(original.Key == row.Key ? FieldValue.Of(999L) : CountValue(original), CountValue(current));
        }
        var exclusive = Assert.IsAssignableFrom<IQueryableDocument>(reopened).Query("hogwarts/RawExclusiveImage/InventoryDynamic",
            new() { Filters = new Dictionary<string, string> { ["SlotNumber"] = "201" } });
        Assert.Equal(201, Assert.IsType<FieldNode>(Assert.Single(exclusive.Rows).Cells["Count"]).Value.AsInt64());
    }

    [Fact]
    public void EditingShownEmptyCountRefreshesVisibilityAndUndoRestoresIt()
    {
        using var import = HogwartsInventoryFixture.CreateDisk();
        var file = import.File;
        var plugin = new UnrealGvasPlugin();
        using var session = new DocumentSession("empty-count", file, plugin, plugin.Open(file));
        _ = session.BuildView();
        var page = session.QueryCollection(HogwartsInventoryFixture.Collection, new() { IncludeEmpty = true, Limit = 1 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        var field = Assert.IsType<FieldNode>(Assert.Single(page.Rows).Cells["Count"]);
        Assert.True(Assert.Single(page.Rows).IsEmpty);
        var edit = session.ApplyEdits([new IncomingEdit(field.ValueId, JsonSerializer.SerializeToElement(1))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(edit.Ok);
        Assert.True(edit.StructureChanged);
        var changed = session.QueryCollection(HogwartsInventoryFixture.Collection, new(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal((208, 202), (changed.FilteredCount, changed.HiddenEmptyCount));
        Assert.True(session.Undo().Ok);
        var undone = session.QueryCollection(HogwartsInventoryFixture.Collection, new(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal((207, 203), (undone.FilteredCount, undone.HiddenEmptyCount));
    }

    [Theory]
    [InlineData(false, 206)]
    [InlineData(true, 409)]
    public void F6SelectAllMatchingExcludesHiddenSlotsAcrossUnloadedPages(bool includeEmpty, int expected)
    {
        using var import = HogwartsInventoryFixture.CreateDisk();
        var file = import.File;
        var plugin = new UnrealGvasPlugin();
        using var session = new DocumentSession("scope", file, plugin, new InventoryScopeDocument(plugin.Open(file)));
        _ = session.BuildView();
        var excluded = session.QueryCollection(HogwartsInventoryFixture.Collection, new() { Limit = 1 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows[0].Key;
        var query = new QueryableCollectionQuery { Text = "Bag", IncludeEmpty = includeEmpty ? true : null };
        var scope = new ActionScope { QueryScope = new(HogwartsInventoryFixture.Collection, query, session.State.Revision) { ExcludedRowKeys = [excluded] } };
        var preview = session.PlanAction(InventoryScopeDocument.ActionId, new Dictionary<string, JsonElement>(), scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.Equal(expected, preview.Scope.RowKeys!.Count);
        Assert.Equal(includeEmpty, preview.Scope.QueryScope!.Query.IncludeEmpty);
        Assert.DoesNotContain(excluded, preview.Scope.RowKeys);
        var shown = session.QueryCollection(HogwartsInventoryFixture.Collection, new() { IncludeEmpty = true, Limit = 200 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.All(shown.Rows.Where(row => row.IsEmpty), row => Assert.Equal(includeEmpty, preview.Scope.RowKeys.Contains(row.Key)));
    }

    [Fact]
    public void NativeAndViewDiffDecodersIncludeAllHiddenSlots()
    {
        var file = HogwartsInventoryFixture.Create();
        var plugin = new UnrealGvasPlugin();
        var native = new HookSaveDecoder(Assert.Single(Assert.IsAssignableFrom<ISaveDecoderProvider>(plugin).Decoders)).Decode(file, new());
        Assert.False(native.Truncated);
        Assert.Equal(820, native.Values.Count(value => value.Path.Contains("/InventoryDynamic/rows/", StringComparison.Ordinal)));
        var view = new PluginViewDecoder(plugin).Decode(file, new());
        Assert.False(view.Truncated);
        Assert.Equal(820, view.Values.Count(value => value.Path.Contains("InventoryDynamic/rows/", StringComparison.Ordinal) && value.Path.EndsWith("/cells/ItemID", StringComparison.Ordinal)));
    }

    private static IReadOnlyList<TableRow> ReadAll(IQueryableDocument document, QueryableCollectionQuery query)
    {
        var rows = new List<TableRow>();
        for (int offset = 0; ; offset += query.Limit)
        {
            var page = document.Query(HogwartsInventoryFixture.Collection, query with { Offset = offset });
            Assert.Equal(HogwartsInventoryFixture.TotalSlots, page.TotalCount);
            Assert.Equal(query.IncludeEmpty == true ? 0 : HogwartsInventoryFixture.EmptySlots, page.HiddenEmptyCount);
            rows.AddRange(page.Rows);
            if (rows.Count >= page.FilteredCount) return rows;
            Assert.NotEmpty(page.Rows);
        }
    }

    private static FieldValue CountValue(TableRow row) => row.Cells["Count"] is FieldNode field
        ? field.Value : FieldValue.Of(Assert.IsType<TextNode>(row.Cells["Count"]).Text);
}
