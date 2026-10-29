using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseRestoreSelectionTests
{
    private const string ActionId = "inventory/restore-imported";
    private const string FirstRow = "inventory/0/quantity";
    private const string SecondRow = "inventory/1/quantity";
    private static readonly IReadOnlyDictionary<string, JsonElement> NoParameters = new Dictionary<string, JsonElement>();

    [Fact]
    public void RestoreControl_IsInsideTheQueryableTableSelectionContext_AndDocumentControlRemains()
    {
        using var document = new TalesOfArisePlugin().Open(MemorySaveSet.SingleFile(
            AriseTestContainer.FileName, new AriseTestContainer().Encrypt(), AriseTestContainer.TitleId));
        var root = Assert.IsType<TabsNode>(document.BuildView());
        var documentAction = Assert.Single(root.Tabs.Single(tab => tab.Title == "Inventory").Children.OfType<ActionNode>(),
            action => action.ActionId == ActionId);
        var table = Assert.IsType<QueryableTableNode>(Assert.Single(document.ExpandNode("inventory")));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        // Include empty and unsupported slots: the control uses the table selection, not its containing row.
        var page = queryable.Query(table.CollectionId, new() { IncludeEmpty = true, Limit = 5, SortColumn = "quantity", Descending = true });
        Assert.Equal(5, page.Rows.Count);
        Assert.Contains(page.Rows, row => row.IsEmpty);
        Assert.Contains(page.Rows, row => row.Key == FirstRow);
        Assert.Contains(page.Rows, row => row.Key == SecondRow);
        foreach (var row in page.Rows)
        {
            var cell = Assert.Single(row.Cells, cell => cell.Value is ActionNode { ActionId: ActionId });
            Assert.Contains(table.Columns, column => column.Key == cell.Key);
            Assert.Contains(Assert.Single(queryable.Collections, collection => collection.Id == table.CollectionId).Columns,
                column => column.Key == cell.Key);
            var action = Assert.IsType<ActionNode>(cell.Value);
            Assert.Equal(documentAction, action);
            Assert.True(action.SupportsPlanning);
            Assert.Empty(action.Parameters);
        }
    }

    [Theory]
    [InlineData("selected", 1, 0)]
    [InlineData("query", 1, 0)]
    [InlineData("query-exclude-all", 0, 0)]
    [InlineData("query-no-matches", 0, 0)]
    [InlineData("document", 1, 15)]
    [InlineData("empty", 0, 0)]
    public void RenderedRestore_RespectsScope_AndUndoRedoRemainOneStep(string mode, long firstQuantity, long secondQuantity)
    {
        var original = new AriseTestContainer().Encrypt();
        var storage = Path.Combine(TestScratch.ProcessFolder, $"restore-selection-{Guid.NewGuid():N}");
        var set = SaveSetImporter.Import([new ImportSource($"{AriseTestContainer.TitleId}/sdimg_generated/{AriseTestContainer.FileName}",
            () => new MemoryStream(original))], storage);
        var file = Assert.Single(set.AllDiskFiles);
        var plugin = new TalesOfArisePlugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("arise-restore-selection", file, plugin, document);
        var root = Assert.IsType<TabsNode>(session.BuildView());
        var table = Assert.IsType<QueryableTableNode>(Assert.Single(session.Expand("inventory")));
        var page = session.QueryCollection(table.CollectionId, new() { Limit = 1 });
        var action = mode == "document"
            ? Assert.Single(root.Tabs.Single(tab => tab.Title == "Inventory").Children.OfType<ActionNode>(), node => node.ActionId == ActionId)
            : Assert.Single(Assert.Single(page.Rows).Cells.Values.OfType<ActionNode>(), node => node.ActionId == ActionId);
        // The first page contains one stack; query scope must resolve the full collection before exclusions.
        _ = session.QueryCollection(table.CollectionId, new() { Text = "Peach Gel" });
        var zero = JsonSerializer.SerializeToElement("0");
        Assert.True(session.ApplyEdits([new(FirstRow, zero), new(SecondRow, zero)]).Ok);
        var reduced = Bytes(document);
        var scope = mode switch
        {
            "selected" => new ActionScope([FirstRow]),
            "empty" => new ActionScope([]),
            "document" => new ActionScope(),
            _ => new ActionScope
            {
                QueryScope = new(table.CollectionId, new() { Text = mode == "query-no-matches" ? "no matching item" : "Gel", Limit = 1 }, session.State.Revision)
                {
                    ExcludedRowKeys = mode == "query-exclude-all"
                        ? [FirstRow, SecondRow, "inventory/2/quantity"]
                        : [SecondRow, "outside-query"],
                },
            },
        };
        var preview = session.PlanAction(action.ActionId, NoParameters, scope);
        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Plan.Errors);
        Assert.Equal(reduced, Bytes(document));
        var expectedChanges = new List<string>();
        if (firstQuantity > 0) expectedChanges.Add(FirstRow);
        if (secondQuantity > 0) expectedChanges.Add(SecondRow);
        Assert.Equal(expectedChanges, preview.Plan.Changes.Where(change => !change.Derived).Select(change => change.ValueId));
        if (scope.QueryScope is not null)
        {
            Assert.NotNull(preview.Scope.RowKeys);
            Assert.DoesNotContain(SecondRow, preview.Scope.RowKeys);
            Assert.Equal(scope.QueryScope.ExcludedRowKeys, preview.Scope.QueryScope!.ExcludedRowKeys);
            if (expectedChanges.Count == 0) Assert.Empty(preview.Scope.RowKeys);
        }
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(firstQuantity, document.GetValue(FirstRow).AsInt64());
        Assert.Equal(secondQuantity, document.GetValue(SecondRow).AsInt64());
        var restored = Bytes(document);
        Assert.True(session.Undo().Ok);
        if (expectedChanges.Count == 0)
        {
            Assert.Equal(original, Bytes(document)); // A no-op adds no history entry.
            Assert.False(session.State.CanUndo);
            Assert.True(session.Redo().Ok);
            Assert.Equal(reduced, Bytes(document));
        }
        else
        {
            Assert.Equal(reduced, Bytes(document));
            Assert.True(session.Redo().Ok);
            Assert.Equal(restored, Bytes(document));
            Assert.True(session.Undo().Ok);
            Assert.True(session.Undo().Ok);
            Assert.Equal(original, Bytes(document));
            Assert.False(session.State.CanUndo);
        }
    }

    private static byte[] Bytes(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();
}
