using System.Buffers.Binary;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Editing;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class ImportedQuantityTests
{
    internal const string ActionId = "inventory/restore-imported-quantities";
    internal static readonly IReadOnlyDictionary<string, FieldValue> NoParameters = new Dictionary<string, FieldValue>();

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void PreviewRestoresOpeningValuesOnlyAndExplainsIneligibleRowsWithoutMutating(int negative)
    {
        var original = Create(10, 20, 0, negative, int.MaxValue, 7, 8);
        using var document = new PragmataDocument("data000.bin", original);
        var tab = ((TabsNode)document.BuildView()).Tabs.Single(tab => tab.Title == "Inventory");
        var action = Assert.Single(tab.Children.OfType<ActionNode>(), node => node.ActionId == ActionId);
        Assert.True(action.SupportsPlanning);
        Assert.Empty(action.Parameters);
        Assert.Contains("Inferred", action.Description, StringComparison.Ordinal);
        var table = Assert.Single(tab.Children.OfType<TableNode>());
        Assert.All(table.Rows, row => Assert.Contains(Assert.IsType<SectionNode>(row.Cells["action"]).Children,
            node => node is ActionNode { ActionId: ActionId, SupportsPlanning: true }));
        var quantities = Quantities(document);
        var raw = document.Field(quantities[0], raw: true);
        Set(document, quantities, 3, 25, 0, -2, 1, -1, 1);
        // A second staged reduction must not replace the immutable opening baseline.
        Assert.True(document.ApplyEdits([new(quantities[0].Path, FieldValue.Of(2))]).Succeeded);
        var itemId = document.Field(document.Layout.Items[6].Find(KnownHashes.ItemId)!, raw: true);
        Assert.True(document.ApplyEdits([new(itemId.ValueId, FieldValue.Of(0))]).Succeeded);
        var before = document.Serialize().Single().Content.ToArray();
        var scope = new ActionScope();
        var plan = document.PlanAction(ActionId, NoParameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(2, plan.Changes.Count); // Two canonical writes shared by every projection.
        Assert.Contains(plan.Changes, change => change.ValueId == quantities[0].Path &&
            change.OldValue.AsInt32() == 2 && change.NewValue.AsInt32() == 10 && !change.Derived);
        Assert.Contains(plan.Changes, change => change.ValueId == quantities[4].Path && change.NewValue.AsInt32() == int.MaxValue);
        Assert.Contains(plan.Changes, change => change.ValueId == raw.ValueId && !change.Derived);
        Assert.All(plan.Changes.Where(change => !change.Derived), change => Assert.Contains("raw item ID 0x", change.Label, StringComparison.Ordinal));
        Assert.Equal(5, plan.SkippedRows.Count);
        Assert.Contains(plan.SkippedRows, row => row.Reason.Contains("above", StringComparison.Ordinal));
        Assert.Contains(plan.SkippedRows, row => row.Reason.Contains("equals", StringComparison.Ordinal));
        Assert.Contains(plan.SkippedRows, row => row.Reason.Contains("opening quantity is negative", StringComparison.Ordinal));
        Assert.Contains(plan.SkippedRows, row => row.Reason.Contains("current quantity is negative", StringComparison.Ordinal));
        Assert.Contains(plan.SkippedRows, row => row.Reason.Contains("ID changed", StringComparison.Ordinal));
        Assert.Contains(plan.Warnings, warning => warning.Contains(PragmataDocument.EditWarning, StringComparison.Ordinal));
        Assert.Contains(plan.Warnings, warning => warning.Contains("opening", StringComparison.Ordinal));
        Assert.Equal(before, document.Serialize().Single().Content.ToArray()); // Cancel = discard preview.
        Assert.True(document.ApplyActionPlan(ActionId, NoParameters, scope, plan).Succeeded);
        Assert.Equal(new[] { 10, 25, 0, -2, int.MaxValue, -1, 1 }, quantities.Select(node => document.Read(node).AsInt32()));
        var again = document.PlanAction(ActionId, NoParameters, scope);
        Assert.Empty(again.Changes);
        Assert.Equal(7, again.SkippedRows.Count);
    }

    [Fact]
    public void InvalidSelectionsAndParametersRejectAtomicallyWhileUnloadedScopesResolveBindings()
    {
        var original = SyntheticInventory.Create(251);
        using var document = new PragmataDocument("data000.bin", original);
        _ = document.BuildView();
        var key = QuantityActions.RowKey(document.Layout.Items[1]);
        foreach (var scope in new[]
        {
            new ActionScope([key, key]), new ActionScope(["unknown"]),
            new ActionScope { QueryScope = new ActionQueryScope(InventoryCollections.CollectionId, new(), 0) },
            new ActionScope([key]) { QueryScope = new ActionQueryScope("evidence", new(), 0) },
        })
        {
            var plan = document.PlanAction(ActionId, NoParameters, scope);
            Assert.NotEmpty(plan.Errors);
            Assert.False(document.ApplyActionPlan(ActionId, NoParameters, scope, plan).Succeeded);
        }
        foreach (var scope in new[] { new ActionScope(), new ActionScope([key]) })
        {
            var plan = document.PlanAction(ActionId, NoParameters, scope);
            Assert.Empty(plan.Errors);
            Assert.Empty(plan.Changes); // Already at the imported baseline; no pages needed.
            Assert.NotEmpty(plan.SkippedRows);
        }
        var empty = document.PlanAction(ActionId, NoParameters, new ActionScope([]));
        Assert.Empty(empty.Errors);
        Assert.Empty(empty.Changes);
        Assert.Empty(empty.SkippedRows);
        _ = document.Query(InventoryCollections.CollectionId, new() { Limit = 2 });
        Assert.NotEmpty(document.PlanAction(ActionId, new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(99) },
            new ActionScope([key])).Errors);
        Assert.Throws<InvalidOperationException>(() => document.InvokeAction(ActionId, NoParameters));
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateCurrentOrOpeningIdsRejectEvenWhenNotSelected(bool importedDuplicate)
    {
        var original = Create(10, 20, 30);
        if (importedDuplicate)
        {
            var container = new DsssContainer(original);
            var plaintext = container.Plaintext.ToArray();
            var layout = new SaveLayout(plaintext);
            BinaryPrimitives.WriteUInt32LittleEndian(plaintext.AsSpan(layout.Items[2].Find(KnownHashes.ItemId)!.Offset), 2);
            original = container.Rebuild(plaintext);
        }
        using var document = new PragmataDocument("data000.bin", original);
        _ = document.BuildView();
        var id = document.Field(document.Layout.Items[2].Find(KnownHashes.ItemId)!, raw: true);
        Assert.True(document.ApplyEdits([new(id.ValueId, FieldValue.Of(importedDuplicate ? 0 : 2))]).Succeeded);
        var before = document.Serialize().Single().Content.ToArray();
        var plan = document.PlanAction(ActionId, NoParameters, new ActionScope([QuantityActions.RowKey(document.Layout.Items[0])]));
        Assert.NotEmpty(plan.Errors);
        Assert.Contains(plan.Errors, error => error.Message.Contains("Duplicate", StringComparison.Ordinal));
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void ForgedAndStalePlansRejectAndReimportEstablishesANewBaseline()
    {
        using var document = new PragmataDocument("data000.bin", Create(10));
        _ = document.BuildView();
        var quantities = Quantities(document);
        Set(document, quantities, 3);
        var scope = new ActionScope();
        var plan = document.PlanAction(ActionId, NoParameters, scope);
        Assert.Empty(plan.Errors);
        var forged = plan with { Changes = [plan.Changes.Single() with { NewValue = FieldValue.Of(11) }] };
        Assert.False(document.ApplyActionPlan(ActionId, NoParameters, scope, forged).Succeeded);
        Set(document, quantities, 2);
        Assert.False(document.ApplyActionPlan(ActionId, NoParameters, scope, plan).Succeeded);
        var bytes = document.Serialize().Single().Content.ToArray();
        using var reopened = new PragmataDocument("data000.bin", bytes);
        _ = reopened.BuildView();
        Assert.Empty(reopened.PlanAction(ActionId, NoParameters, scope).Changes);
        Set(reopened, Quantities(reopened), 1);
        Assert.Equal(2, Assert.Single(reopened.PlanAction(ActionId, NoParameters, scope).Changes).NewValue.AsInt32());
    }

    internal static SaveNode[] Quantities(PragmataDocument document) =>
        document.Layout.Items.Select(item => item.Find(KnownHashes.Quantity)!).ToArray();

    internal static void Set(PragmataDocument document, SaveNode[] nodes, params int[] values) =>
        Assert.True(document.ApplyEdits(nodes.Zip(values, (node, value) => new ValueEdit(node.Path, FieldValue.Of(value))).ToArray()).Succeeded);

    internal static byte[] Create(params int[] quantities)
    {
        var container = new DsssContainer(SyntheticInventory.Create(quantities.Length));
        var plaintext = container.Plaintext.ToArray();
        var nodes = new SaveLayout(plaintext).Items.Select(item => item.Find(KnownHashes.Quantity)!).ToArray();
        for (var index = 0; index < quantities.Length; index++)
            BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(nodes[index].Offset), quantities[index]);
        return container.Rebuild(plaintext);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EmptyInventoryAndOpeningZeroQuantityRemainByteIdentical(int count)
    {
        var original = SyntheticInventory.Create(count);
        using var document = new PragmataDocument("data000.bin", original);
        _ = document.BuildView();
        var scope = new ActionScope();
        var plan = document.PlanAction(ActionId, NoParameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Equal(count, plan.SkippedRows.Count);
        var applied = document.ApplyActionPlan(ActionId, NoParameters, scope, plan);
        Assert.True(applied.Succeeded);
        Assert.Empty(applied.Changes);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }
}
