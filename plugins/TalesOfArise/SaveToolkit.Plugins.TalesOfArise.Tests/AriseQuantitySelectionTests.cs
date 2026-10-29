using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseQuantitySelectionTests
{
    private const string ActionId = "inventory/set-quantity";
    private const string CollectionId = "inventory";
    private const string QuantityParameter = "quantity";
    private const string FirstRow = "inventory/0/quantity";
    private const string SecondRow = "inventory/1/quantity";
    private const string OverBoundRow = "inventory/2/quantity";
    private const string UnknownRow = "inventory/3/quantity";
    private const string EmptyRow = "inventory/4/quantity";
    private const long TargetQuantity = 12;
    private const long StackLimit = 15;

    [Fact]
    public void PublishedSetControl_BindsToTheExactInventoryCollection_AndRetainsParameterIdentity()
    {
        var original = new AriseTestContainer().Encrypt();
        using var document = new TalesOfArisePlugin().Open(MemorySaveSet.SingleFile(
            AriseTestContainer.FileName, original, AriseTestContainer.TitleId));
        var action = SetControl(document.BuildView());
        Assert.True(action.SupportsPlanning);
        Assert.Equal(CollectionId, action.SelectionTableId);
        var parameter = Assert.Single(action.Parameters);
        Assert.Equal(QuantityParameter, parameter.ValueId);
        var spec = Assert.IsType<IntegerSpec>(parameter.Spec);
        Assert.Equal((Int128)0, spec.Min);
        Assert.Equal((Int128)StackLimit, spec.Max);
        Assert.Equal(StackLimit, parameter.Value.AsInt64());
        var table = Assert.IsType<QueryableTableNode>(Assert.Single(document.ExpandNode(CollectionId)));
        Assert.Equal(table.CollectionId, action.SelectionTableId);
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(action), JsonSerializer.SerializeToUtf8Bytes(SetControl(document.BuildView())));
        Assert.Equal(original, Bytes(document));
    }

    [Theory]
    [InlineData("selected", TargetQuantity, TargetQuantity, 15)]
    [InlineData("selected-unloaded", TargetQuantity, 1, TargetQuantity)]
    [InlineData("query", TargetQuantity, TargetQuantity, TargetQuantity)]
    [InlineData("query-excluded", TargetQuantity, 1, TargetQuantity)]
    [InlineData("query-exclude-all", TargetQuantity, 1, 15)]
    [InlineData("query-no-matches", TargetQuantity, 1, 15)]
    [InlineData("query-empty-slots", TargetQuantity, TargetQuantity, TargetQuantity)]
    [InlineData("document", TargetQuantity, TargetQuantity, TargetQuantity)]
    [InlineData("empty", TargetQuantity, 1, 15)]
    [InlineData("unchanged", 1, 1, 15)]
    public void BoundControl_PreviewsExactScope_PreservesBytes_AndUsesOneUndoGroup(
        string mode, long target, long firstQuantity, long secondQuantity)
    {
        var fixture = new AriseTestContainer();
        var original = fixture.Encrypt();
        using var context = new SessionContext(original);
        var session = context.Session;
        var document = context.Document;
        var action = SetControl(session.BuildView());
        var table = Assert.IsType<QueryableTableNode>(Assert.Single(session.Expand(CollectionId)));
        Assert.Equal(table.CollectionId, action.SelectionTableId);
        var page = session.QueryCollection(table.CollectionId, new() { Limit = 1 },
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(FirstRow, Assert.Single(page.Rows).Key);
        Assert.DoesNotContain(page.Rows, row => row.Key == SecondRow);
        Assert.Equal(FirstRow, Assert.IsType<FieldNode>(page.Rows[0].Cells[QuantityParameter]).ValueId);

        var scope = Scope(mode, session.State.Revision);
        var preview = session.PlanAction(action.ActionId, Parameters(target), scope,
            expectedRevision: session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Plan.Errors);
        Assert.Equal(session.State.Revision, preview.Revision);
        Assert.Equal(original, Bytes(document));
        Assert.False(session.State.CanUndo);
        var expectedChanges = new List<string>();
        if (firstQuantity != 1) expectedChanges.Add(FirstRow);
        if (secondQuantity != StackLimit) expectedChanges.Add(SecondRow);
        Assert.Equal(expectedChanges, preview.Plan.Changes.Where(change => !change.Derived).Select(change => change.ValueId));
        Assert.All(preview.Plan.Changes.Where(change => !change.Derived), change => Assert.Equal(target, change.NewValue.AsInt64()));
        if (scope.QueryScope is { } query)
        {
            var resolved = Assert.IsType<ActionQueryScope>(preview.Scope.QueryScope);
            Assert.Equal(query.Revision, resolved.Revision);
            Assert.Equal(query.ExcludedRowKeys, resolved.ExcludedRowKeys);
            Assert.Equal(query.Query.Text, resolved.Query.Text);
            Assert.Equal(query.Query.SortColumn, resolved.Query.SortColumn);
            Assert.Equal(query.Query.Descending, resolved.Query.Descending);
            Assert.Equal(query.Query.IncludeEmpty, resolved.Query.IncludeEmpty);
            Assert.Equal(query.Query.Filters, resolved.Query.Filters);
            Assert.Equal(0, resolved.Query.Offset);
            Assert.NotNull(preview.Scope.RowKeys);
            Assert.All(expectedChanges, key => Assert.Contains(key, preview.Scope.RowKeys));
            Assert.All(query.ExcludedRowKeys, key => Assert.DoesNotContain(key, preview.Scope.RowKeys));
            if (expectedChanges.Count == 0) Assert.Empty(preview.Scope.RowKeys);
        }
        else
        {
            Assert.Equal(scope.RowKeys, preview.Scope.RowKeys);
        }
        if (mode == "query-empty-slots")
        {
            Assert.Equal(Assert.Single(((IQueryableDocument)document).Collections,
                collection => collection.Id == CollectionId).TotalCount, preview.Scope.RowKeys!.Count);
            Assert.Contains(preview.Plan.SkippedRows, row => row.RowKey == EmptyRow);
            Assert.Contains(preview.Plan.SkippedRows, row => row.RowKey == UnknownRow);
        }
        if (mode is "query" or "query-empty-slots" or "document")
            Assert.Contains(preview.Plan.SkippedRows, row => row.RowKey == OverBoundRow);

        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision,
            cancellationToken: TestContext.Current.CancellationToken).Ok);
        // Bind populated rows after apply; empty slots would fill this small ID-sorted page.
        _ = session.QueryCollection(CollectionId, new() { Limit = 5, IncludeEmpty = false },
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(firstQuantity, document.GetValue(FirstRow).AsInt64());
        Assert.Equal(secondQuantity, document.GetValue(SecondRow).AsInt64());
        var expected = fixture.Payload.ToArray();
        var inventory = fixture.Body("SaveData_ItemManager");
        expected[inventory] = checked((byte)firstQuantity);
        expected[inventory + AriseTestContainer.ItemRecordSize] = checked((byte)secondQuantity);
        var output = Bytes(document);
        Assert.Equal(expected.AsSpan(0x20).ToArray(), AriseFormat.Decode(output).Payload.AsSpan(0x20).ToArray());
        Assert.Equal(original.AsSpan(0, AriseFormat.PayloadOffset).ToArray(), output.AsSpan(0, AriseFormat.PayloadOffset).ToArray());
        if (expectedChanges.Count == 0)
        {
            Assert.Equal(original, output);
            Assert.False(session.State.CanUndo);
            Assert.False(session.State.CanRedo);
            Assert.False(session.State.IsDirty);
            return;
        }

        Assert.True(session.State.CanUndo);
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Bytes(document));
        Assert.False(session.State.CanUndo);
        Assert.True(session.Redo().Ok);
        Assert.Equal(output, Bytes(document));
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Bytes(document));
        Assert.False(session.State.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectionPreview_RejectsInterveningRevision_WithoutApplyingQuantities(bool query)
    {
        var original = new AriseTestContainer().Encrypt();
        using var context = new SessionContext(original);
        var session = context.Session;
        var action = SetControl(session.BuildView());
        _ = session.Expand(CollectionId);
        var scope = Scope(query ? "query" : "selected", session.State.Revision);
        var preview = session.PlanAction(action.ActionId, Parameters(TargetQuantity), scope,
            expectedRevision: session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Plan.Errors);
        Assert.True(session.ApplyEdits([new("party/gald", JsonSerializer.SerializeToElement("124"))],
            expectedRevision: preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var edited = Bytes(context.Document);
        var rejected = session.ApplyActionPlan(preview.PlanId!, preview.Revision,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(rejected.Ok);
        Assert.True(rejected.Stale);
        Assert.Equal(edited, Bytes(context.Document));
        if (query)
        {
            var staleScope = session.PlanAction(action.ActionId, Parameters(TargetQuantity), scope,
                expectedRevision: session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(staleScope.Stale);
            Assert.Null(staleScope.PlanId);
            Assert.NotEmpty(staleScope.Plan.Errors);
            Assert.Equal(edited, Bytes(context.Document));
        }
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Bytes(context.Document));
        Assert.False(session.State.CanUndo);
    }

    [Fact]
    public void EmptySelection_PreservesExistingUndoAndRedoHistory()
    {
        var original = new AriseTestContainer().Encrypt();
        using var context = new SessionContext(original);
        var session = context.Session;
        var action = SetControl(session.BuildView());
        _ = session.Expand(CollectionId);
        Assert.True(session.ApplyEdits([new("party/gald", JsonSerializer.SerializeToElement("124"))],
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var edited = Bytes(context.Document);
        ApplyEmptySelection();
        Assert.True(session.State.CanUndo);
        Assert.False(session.State.CanRedo);
        Assert.True(session.Undo().Ok);
        ApplyEmptySelection();
        Assert.False(session.State.CanUndo);
        Assert.True(session.State.CanRedo);
        Assert.Equal(original, Bytes(context.Document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Bytes(context.Document));

        void ApplyEmptySelection()
        {
            var before = Bytes(context.Document);
            var preview = session.PlanAction(action.ActionId, Parameters(TargetQuantity), new ActionScope([]),
                expectedRevision: session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(preview.PlanId);
            Assert.Empty(preview.Plan.Errors);
            Assert.Empty(preview.Plan.Changes);
            Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision,
                cancellationToken: TestContext.Current.CancellationToken).Ok);
            Assert.Equal(before, Bytes(context.Document));
        }
    }

    private static ActionScope Scope(string mode, long revision) => mode switch
    {
        "selected" or "unchanged" => new([FirstRow]),
        "selected-unloaded" => new([SecondRow]),
        "empty" => new([]),
        "document" => new(),
        _ => new()
        {
            QueryScope = new(CollectionId, new()
            {
                Text = mode == "query-no-matches" ? "no matching item" : mode == "query-empty-slots" ? null : "Gel",
                Filters = mode == "query-empty-slots" ? new Dictionary<string, string>()
                    : new Dictionary<string, string> { ["flags"] = "0x80" },
                SortColumn = "id", Descending = false, IncludeEmpty = mode == "query-empty-slots", Offset = 1, Limit = 1,
            }, revision)
            {
                ExcludedRowKeys = mode switch
                {
                    "query-excluded" => [FirstRow, "outside-query"],
                    "query-exclude-all" => [FirstRow, SecondRow, OverBoundRow],
                    _ => [],
                },
            },
        },
    };

    private static ActionNode SetControl(ViewNode root) => Assert.Single(
        Assert.IsType<TabsNode>(root).Tabs.Single(tab => tab.Title == "Inventory").Children.OfType<ActionNode>(),
        action => action.ActionId == ActionId);

    private static IReadOnlyDictionary<string, JsonElement> Parameters(long quantity) =>
        new Dictionary<string, JsonElement> { [QuantityParameter] = JsonSerializer.SerializeToElement(quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)) };

    private static byte[] Bytes(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();

    private sealed class SessionContext : IDisposable
    {
        public ISaveDocument Document { get; }
        public DocumentSession Session { get; }

        public SessionContext(byte[] original)
        {
            var storage = Path.Combine(TestScratch.ProcessFolder, $"quantity-selection-{Guid.NewGuid():N}");
            var set = SaveSetImporter.Import([new ImportSource(
                $"{AriseTestContainer.TitleId}/sdimg_generated/{AriseTestContainer.FileName}", () => new MemoryStream(original))],
                storage, limits: null, cancellationToken: TestContext.Current.CancellationToken);
            var file = Assert.Single(set.AllDiskFiles);
            var plugin = new TalesOfArisePlugin();
            Document = plugin.Open(file);
            Session = new DocumentSession("arise-quantity-selection", file, plugin, Document);
        }

        public void Dispose() => Session.Dispose();
    }
}
