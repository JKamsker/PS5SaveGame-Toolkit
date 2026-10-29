using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class SelectionDeclarationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StructuralControls_InheritEachItemTableWithoutChangingDocumentActions(bool wonderlands, bool profile)
    {
        using var document = SelectionTestData.Open(wonderlands, profile);
        var before = SelectionTestData.Bytes(document);
        var inventory = Assert.IsType<TabsNode>(document.BuildView()).Tabs.Single(tab => tab.Title == "Inventory");
        Assert.DoesNotContain(inventory.Children.OfType<ActionNode>(), action => action.ActionId is "items/duplicate" or "items/remove");
        Assert.All(inventory.Children.OfType<ActionNode>(), action => Assert.Null(action.SelectionTableId));
        Assert.Contains(inventory.Children.OfType<ActionNode>(), action => action.ActionId == "items/import" && action.SupportsPlanning);
        Assert.Contains(inventory.Children.OfType<ActionNode>(), action => action.ActionId == "items/level" && action.SupportsPlanning);
        Assert.All(inventory.Children.OfType<ActionNode>().Where(action => action.ActionId.StartsWith("catalogue/", StringComparison.Ordinal)),
            action => { Assert.False(action.SupportsPlanning); Assert.False(action.Mutates); });

        foreach (var table in inventory.Children.OfType<QueryableTableNode>().Where(table => table.CollectionId != "catalogue"))
        {
            var page = document.Query(table.CollectionId, new() { Limit = 2 });
            Assert.Equal(SelectionTestData.ItemCount, page.TotalCount);
            foreach (var row in page.Rows)
                AssertStructuralActions(SelectionTestData.Details(document, row), "");
        }
        SelectionTestData.AssertBytes(document, before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupControls_DeclareExactCompositeSourcesAndLocalizeCurrentRows(bool wonderlands)
    {
        using var group = SelectionTestData.Group(wonderlands);
        var before = SelectionTestData.Bytes(group);
        var root = Assert.IsType<SectionNode>(group.BuildView());
        var expected = new Dictionary<string, string>
        {
            ["transfer/deposit"] = "character/inventory_items",
            ["transfer/withdraw"] = "profile/bank_inventory_list",
            ["transfer/claim"] = "profile/lost_loot_inventory_list",
        };
        var index = new ViewIndex(group);
        index.Add(root);
        Assert.Empty(index.SelectionDiagnostics);
        // Moving the actions must not renumber the group's traversal-scoped eager equipment/SDU rows.
        var legacy = LegacySelectionPresentation.View(group);
        Assert.Equal(GearboxGroupTests.Walk(group, legacy).OfType<TableNode>().SelectMany(table => table.Rows.Select(row => row.Key)),
            GearboxGroupTests.Walk(group, root).OfType<TableNode>().SelectMany(table => table.Rows.Select(row => row.Key)));
        Assert.Equal(expected.Count, root.Children.OfType<ActionNode>().Count());
        foreach (var action in root.Children.OfType<ActionNode>())
        {
            Assert.True(action.SupportsPlanning);
            Assert.Equal(expected[action.ActionId], action.SelectionTableId);
            Assert.Single(group.Collections, collection => collection.Id == action.SelectionTableId);
        }

        foreach (var collection in expected.Values)
        {
            var role = collection.Split('/')[0];
            var member = role == "character" ? group.Character : group.Profile;
            var adapter = new DocumentMemberAdapter(role, member, GearboxGroupIdentity.Policy);
            var page = group.Query(collection, new() { Limit = 2 });
            Assert.All(page.Rows, row => Assert.StartsWith(collection + "/item-", row.Key, StringComparison.Ordinal));
            AssertStructuralActions(SelectionTestData.Details(group, page.Rows[0]), role + "/");
            var query = new ActionQueryScope(collection, new() { Limit = 1 }, 17) { ExcludedRowKeys = [page.Rows[1].Key] };
            var local = adapter.ToLocalScope(new([page.Rows[0].Key]) { QueryScope = query });
            Assert.Equal([page.Rows[0].Key[(collection.Length + 1)..]], local.RowKeys);
            // The Gearbox policy preserves composite exclusions as metadata: Core resolved them already.
            Assert.Equal(query with { CollectionId = collection[(role.Length + 1)..] }, local.QueryScope);
            Assert.Null(adapter.ToLocalScope(new()).RowKeys);
            Assert.Empty(adapter.ToLocalScope(new([])).RowKeys!);
        }
        SelectionTestData.AssertBytes(group, before);
    }

    internal static void AssertStructuralActions(IReadOnlyList<ViewNode> details, string prefix)
    {
        foreach (var id in new[] { "items/duplicate", "items/remove" })
        {
            var action = Assert.Single(details.OfType<ActionNode>(), node => node.ActionId == prefix + id);
            Assert.True(action.SupportsPlanning);
            Assert.True(action.Mutates);
            Assert.Null(action.SelectionTableId); // The enclosing source table supplies its selection.
            Assert.Empty(action.Parameters);
            Assert.Contains(SerialWrites.Warning, action.Description, StringComparison.Ordinal);
        }
    }
}
