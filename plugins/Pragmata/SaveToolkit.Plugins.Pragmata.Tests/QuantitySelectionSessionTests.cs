using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Editing;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class QuantitySelectionSessionTests
{
    private const int LargeInventorySize = 251;
    private const int OpeningQuantity = 10;
    private const int ReducedQuantity = 1;
    private const int TargetQuantity = 17;
    private const string ExplicitEmpty = "explicit";
    private const string NoMatches = "no-match";
    private const string FullyExcluded = "excluded";

    public enum ScopeKind { Explicit, Document, Query }

    [Theory]
    [InlineData(3, false, ScopeKind.Explicit)]
    [InlineData(3, true, ScopeKind.Explicit)]
    [InlineData(3, false, ScopeKind.Document)]
    [InlineData(3, true, ScopeKind.Document)]
    [InlineData(LargeInventorySize, false, ScopeKind.Explicit)]
    [InlineData(LargeInventorySize, true, ScopeKind.Explicit)]
    [InlineData(LargeInventorySize, false, ScopeKind.Document)]
    [InlineData(LargeInventorySize, true, ScopeKind.Document)]
    [InlineData(LargeInventorySize, false, ScopeKind.Query)]
    [InlineData(LargeInventorySize, true, ScopeKind.Query)]
    public void BoundActionsPreserveSelectionAliasesPreviewAndExactUndoRedo(int count, bool restore, ScopeKind kind)
    {
        var original = Create(count);
        using var file = new SyntheticSessionFile(original);
        using var document = new PragmataDocument(file.File.RelativePath, original);
        using var session = new DocumentSession("bound-quantity-selection", file.File, new PragmataPlugin(), document);
        var control = Toolbar(session, restore);
        var quantities = ImportedQuantityTests.Quantities(document);
        if (count == LargeInventorySize) Assert.All(quantities, quantity => Assert.False(document.HasPrimaryBinding(quantity)));
        StageReductions(session, quantities.Select(quantity => quantity.Path));
        var before = document.Serialize().Single().Content.ToArray();
        var selected = kind switch
        {
            ScopeKind.Document => document.Layout.Items.ToArray(),
            ScopeKind.Query => document.Layout.Items.Take(count - 1).ToArray(),
            _ => [document.Layout.Items[0], document.Layout.Items[^1]],
        };
        var selectedKeys = selected.Select(QuantityActions.RowKey).ToHashSet(StringComparer.Ordinal);
        var selectedIds = selected.Select(item => item.Find(KnownHashes.Quantity)!.Path).ToHashSet(StringComparer.Ordinal);
        var primaryId = selectedIds.Order(StringComparer.Ordinal).Last();
        string[] aliases = [$"raw/{primaryId}", $"inspect/value/{primaryId}"];
        var metadata = document.ResolveFieldMetadata([primaryId, .. aliases]);
        Assert.Equal(FieldBindingKind.Primary, metadata[0].BindingKind);
        Assert.False(metadata[0].Spec.ReadOnly);
        Assert.All(metadata.Skip(1), field =>
        {
            Assert.Equal(FieldBindingKind.Derived, field.BindingKind);
            Assert.True(field.Spec.ReadOnly);
        });
        var scope = kind == ScopeKind.Query ? new ActionScope
        {
            QueryScope = new ActionQueryScope(control.SelectionTableId!, new QueryableCollectionQuery
            {
                Text = "unmapped", Filters = new Dictionary<string, string> { ["quantity"] = "1" },
                SortColumn = "quantity", Descending = true, Offset = 100, Limit = 7,
            }, session.State.Revision) { ExcludedRowKeys = [QuantityActions.RowKey(document.Layout.Items[^1])] },
        } : kind == ScopeKind.Document ? new ActionScope() : new ActionScope(selected.Select(QuantityActions.RowKey).ToArray());
        var parameters = Parameters(restore);
        Assert.False(session.InvokeAction(control.ActionId, parameters, expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken).Ok);
        var preview = session.PlanAction(control.ActionId, parameters, scope, expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        if (kind == ScopeKind.Document) Assert.Null(preview.Scope.RowKeys);
        else Assert.True(selectedKeys.SetEquals(preview.Scope.RowKeys!));
        if (kind == ScopeKind.Query)
        {
            var query = Assert.IsType<ActionQueryScope>(preview.Scope.QueryScope);
            Assert.Equal(0, query.Query.Offset);
            Assert.Equal("unmapped", query.Query.Text);
            Assert.Equal("1", query.Query.Filters["quantity"]);
            Assert.Equal("quantity", query.Query.SortColumn);
            Assert.True(query.Query.Descending);
            Assert.Equal(QuantityActions.RowKey(document.Layout.Items[^1]), Assert.Single(query.ExcludedRowKeys));
            Assert.Equal(selectedKeys.Order(StringComparer.Ordinal), preview.Scope.RowKeys);
        }
        Assert.True(selectedIds.SetEquals(preview.Plan.Changes.Where(change => !change.Derived).Select(change => change.ValueId)));
        Assert.Equal(aliases, preview.Plan.Changes.Where(change => change.Derived).Select(change => change.ValueId));
        Assert.Equal(selected.Length + aliases.Length, preview.Plan.Changes.Count);
        Assert.Empty(preview.Plan.SkippedRows);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.False(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var target = restore ? OpeningQuantity : TargetQuantity;
        Assert.All(quantities, quantity => Assert.Equal(selectedIds.Contains(quantity.Path) ? target : ReducedQuantity,
            document.Read(quantity).AsInt32()));
        Assert.All(aliases, id => Assert.Equal(target, document.GetValue(id).AsInt32()));
        var container = new DsssContainer(before);
        var expected = container.Plaintext.ToArray();
        foreach (var quantity in quantities.Where(quantity => selectedIds.Contains(quantity.Path)))
            BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(quantity.Offset), target);
        var modified = session.Serialize(cancellationToken: TestContext.Current.CancellationToken).Single().Content.ToArray();
        Assert.Equal(container.Rebuild(expected), modified); // Includes unknown bytes, encryption and integrity metadata.
        using var reopened = new PragmataDocument(file.File.RelativePath, modified);
        Assert.Equal(expected, new DsssContainer(reopened.Serialize().Single().Content.Span).Plaintext);
        Assert.Equal(modified, reopened.Serialize().Single().Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(modified, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.True(session.Undo().Ok); // One undo for the action, one for staging; derived aliases are never replayed.
        Assert.False(session.State.CanUndo);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(3, false, ExplicitEmpty)]
    [InlineData(3, true, ExplicitEmpty)]
    [InlineData(LargeInventorySize, false, ExplicitEmpty)]
    [InlineData(LargeInventorySize, true, ExplicitEmpty)]
    [InlineData(LargeInventorySize, false, NoMatches)]
    [InlineData(LargeInventorySize, true, NoMatches)]
    [InlineData(LargeInventorySize, false, FullyExcluded)]
    [InlineData(LargeInventorySize, true, FullyExcluded)]
    public void EmptyBoundSelectionsNeverBecomeDocumentMutations(int count, bool restore, string kind)
    {
        var original = Create(count);
        using var file = new SyntheticSessionFile(original);
        using var document = new PragmataDocument(file.File.RelativePath, original);
        using var session = new DocumentSession("empty-bound-selection", file.File, new PragmataPlugin(), document);
        var control = Toolbar(session, restore);
        StageReductions(session, ImportedQuantityTests.Quantities(document).Select(quantity => quantity.Path));
        var before = document.Serialize().Single().Content.ToArray();
        var scope = kind == ExplicitEmpty ? new ActionScope([]) : new ActionScope
        {
            QueryScope = new ActionQueryScope(control.SelectionTableId!,
                new QueryableCollectionQuery { Text = kind == NoMatches ? "no-such-item" : null }, session.State.Revision)
            {
                ExcludedRowKeys = kind == FullyExcluded ? document.Layout.Items.Select(QuantityActions.RowKey).ToArray() : [],
            },
        };
        var preview = session.PlanAction(control.ActionId, Parameters(restore), scope, expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Scope.RowKeys!);
        Assert.Empty(preview.Plan.Changes);
        Assert.Empty(preview.Plan.SkippedRows);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Undo().Ok); // Empty apply adds no undo entry.
        Assert.False(session.State.CanUndo);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundWholeQueryStillRejectsMatchingRowsBeyondHostBudgetBeforeExclusions(bool restore)
    {
        const int maximumRows = LargeInventorySize - 1;
        var original = Create(LargeInventorySize);
        using var file = new SyntheticSessionFile(original);
        using var document = new PragmataDocument(file.File.RelativePath, original);
        using var session = new DocumentSession("bounded-selection", file.File, new PragmataPlugin(), document,
            new DocumentSessionOptions { MaxQueryActionRows = maximumRows });
        var control = Toolbar(session, restore);
        StageReductions(session, ImportedQuantityTests.Quantities(document).Select(quantity => quantity.Path));
        var before = document.Serialize().Single().Content.ToArray();
        var scope = new ActionScope
        {
            QueryScope = new ActionQueryScope(control.SelectionTableId!, new(), session.State.Revision)
            {
                ExcludedRowKeys = [QuantityActions.RowKey(document.Layout.Items[^1])],
            },
        };
        var preview = session.PlanAction(control.ActionId, Parameters(restore), scope, expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(preview.PlanId);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.Contains(preview.Plan.Errors, error => error.Message.Contains("matching rows", StringComparison.Ordinal));
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    private static byte[] Create(int count) => ImportedQuantityTests.Create(Enumerable.Repeat(OpeningQuantity, count).ToArray());

    private static Dictionary<string, JsonElement> Parameters(bool restore) => restore ? []
        : new() { ["quantity"] = JsonSerializer.SerializeToElement(TargetQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)) };

    private static ActionNode Toolbar(DocumentSession session, bool restore)
    {
        var view = Assert.IsType<TabsNode>(session.BuildView());
        var tab = view.Tabs.Single(tab => tab.Title == "Inventory");
        var control = Assert.Single(tab.Children.OfType<ActionNode>(), action => action.ActionId ==
            (restore ? PragmataViews.RestoreQuantityAction : PragmataViews.QuantityAction));
        Assert.True(control.SupportsPlanning);
        Assert.Equal(InventoryCollections.CollectionId, control.SelectionTableId);
        return control;
    }

    private static void StageReductions(DocumentSession session, IEnumerable<string> ids) =>
        Assert.True(session.ApplyEdits(ids.Select(id => ImportedQuantitySessionTests.Edit(id, ReducedQuantity)).ToArray(),
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
}
