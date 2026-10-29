using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class InventoryCollectionTests
{
    [Fact]
    public void LargeInventoryPagesBeforeBuildingFieldsAndSupportsLiveQueries()
    {
        using var document = new PragmataDocument("data000.bin", SyntheticInventory.Create(251));
        var tab = ((TabsNode)document.BuildView()).Tabs.Single(tab => tab.Title == "Inventory");
        var table = Assert.Single(tab.Children.OfType<QueryableTableNode>());
        Assert.Equal(251, table.TotalCount);
        Assert.Empty(tab.Children.OfType<TableNode>());
        Assert.Contains(document.Collections, collection => collection.Id == table.CollectionId);
        var last = document.Layout.Items[^1];
        Assert.False(document.HasPrimaryBinding(last.Find(KnownHashes.Quantity)!));
        var query = new QueryableCollectionQuery { Limit = 200, SortColumn = "quantity", Descending = true };
        var page = document.Query(table.CollectionId, query);
        Assert.Equal(200, page.Rows.Count);
        var quantities = page.Rows.Select(row => ((FieldNode)row.Cells["quantity"]).Value.AsInt32()).ToArray();
        Assert.Equal(quantities.OrderDescending(), quantities);
        var second = document.Query(table.CollectionId, query with { Offset = 200 });
        Assert.Equal(51, second.Rows.Count);
        Assert.Equal(251, page.Rows.Concat(second.Rows).Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
        var field = Assert.IsType<FieldNode>(page.Rows[0].Cells["quantity"]);
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(100L))]).Succeeded);
        var filtered = document.Query(table.CollectionId, query with { Filters = new Dictionary<string, string> { ["quantity"] = "100" } });
        Assert.Equal(page.Rows[0].Key, Assert.Single(filtered.Rows).Key);
        Assert.Equal(1, document.Query(table.CollectionId, query with { Text = "100" }).Rows.Count(row => row.Key == page.Rows[0].Key));
        foreach (var invalid in new[] { query with { Limit = 201 }, query with { Offset = -1 }, query with { Cursor = "x" }, query with { SortColumn = "missing" } })
            Assert.ThrowsAny<ArgumentException>(() => document.Query(table.CollectionId, invalid));
    }

    [Fact]
    public void WholeQueryPublishesUnloadedBindingsAndAppliesUndoesAndRedoesAsOneStep()
    {
        var original = SyntheticInventory.Create(251);
        using var fixture = new SyntheticSessionFile(original);
        var file = fixture.File;
        using var document = new PragmataDocument(file.RelativePath, original);
        var plugin = new PragmataPlugin();
        using var session = new DocumentSession("large-inventory", file, plugin, document);
        _ = session.BuildView();
        var parameters = new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement("17") };
        Assert.Null(session.PlanAction(PragmataViews.QuantityAction, parameters, new ActionScope()).PlanId);
        var query = new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["category"] = "unmapped" }, SortColumn = "quantity" };
        var excluded = document.Layout.Items[0];
        var excludedKey = Editing.QuantityActions.RowKey(excluded);
        var scope = new ActionScope { QueryScope = new ActionQueryScope(InventoryCollections.CollectionId, query, session.State.Revision)
            { ExcludedRowKeys = [excludedKey] } };
        var preview = session.PlanAction(PragmataViews.QuantityAction, parameters, scope);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(250, preview.Plan.Changes.Count);
        Assert.All(document.Layout.Items, item => Assert.True(document.HasPrimaryBinding(item.Find(KnownHashes.Quantity)!)));
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        Assert.Equal(0, document.Read(excluded.Find(KnownHashes.Quantity)!).AsInt32());
        Assert.All(document.Layout.Items.Skip(1), item => Assert.Equal(17, document.Read(item.Find(KnownHashes.Quantity)!).AsInt32()));
        var modified = session.Serialize().Single().Content.ToArray();
        using var reopened = new PragmataDocument(file.RelativePath, modified);
        Assert.All(reopened.Layout.Items.Skip(1), item => Assert.Equal(17, reopened.Read(item.Find(KnownHashes.Quantity)!).AsInt32()));
        var before = new DsssContainer(original).Plaintext;
        var after = new DsssContainer(modified).Plaintext;
        var diff = ByteDiffer.Compare(before, after, options: new ByteDiffOptions { MergeGap = 0 });
        Assert.Equal(diff.RangeCount, diff.Ranges.Count);
        Assert.All(diff.Ranges, range => Assert.Contains(document.Layout.Items.Skip(1).Select(item => item.Find(KnownHashes.Quantity)!),
            quantity => range.Offset >= quantity.Offset && range.Offset + range.Length <= quantity.Offset + quantity.Size));
        var review = session.GetChanges();
        Assert.True(review.Complete);
        Assert.Equal(250, review.Changes.Count);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(modified, document.Serialize().Single().Content.ToArray());
        Assert.Null(session.PlanAction(PragmataViews.QuantityAction, parameters, scope).PlanId);
    }

    [Fact]
    public void FloatBoundsAreObservedAndSharedAcrossAliasesWithoutAllowingNativeMaxima()
    {
        using var document = new PragmataDocument("data000.bin", SyntheticInventory.Create(251));
        var node = document.Layout.Items[0].Find(SyntheticInventory.FloatHash)!;
        var field = document.Field(node);
        var spec = Assert.IsType<FloatSpec>(field.Spec);
        Assert.Equal(-2.5, spec.Min);
        Assert.Equal(4.75, spec.Max);
        Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(4.751))]).Succeeded);
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(1.25))]).Succeeded);
        Assert.Equal(spec, document.Bounds.Spec(node));
        Assert.Equal(spec, document.Field(node, raw: true).Spec);
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
    }
}
