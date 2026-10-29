using System.Text.Json;
using SaveToolkit.Core;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Tests.SdkHelpers;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SelectionTableBindingTests
{
    [Fact]
    public void OptionalIdsRoundTripWithoutChangingConstructorsOrDefaults()
    {
        var action = new ActionNode("restore", "Restore");
        var table = new TableNode([], []);
        Assert.Null(action.SelectionTableId);
        Assert.Null(table.SelectionId);
        ViewNode root = new SectionNode("Inventory", [action with { SelectionTableId = "items" }, table with { SelectionId = "items" }]);
        var json = JsonSerializer.Serialize(root, ToolkitJson.Options);
        var result = Assert.IsType<SectionNode>(JsonSerializer.Deserialize<ViewNode>(json, ToolkitJson.Options));
        Assert.Equal("items", Assert.IsType<ActionNode>(result.Children[0]).SelectionTableId);
        Assert.Equal("items", Assert.IsType<TableNode>(result.Children[1]).SelectionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MemberAdapterMapsTableAndActionIdsWithTheIdentityCodec(bool queryable)
    {
        using var document = new MemberAdapterDocument();
        var adapter = new DocumentMemberAdapter("profile", document, new DocumentMemberIdentityPolicy
        {
            ToGroupId = static (role, id) => role + ":" + Uri.EscapeDataString(id),
            TryToLocalId = static (role, id) => id.StartsWith(role + ":", StringComparison.Ordinal) ? Uri.UnescapeDataString(id[(role.Length + 1)..]) : null,
        });
        ViewNode table = queryable ? new QueryableTableNode("items/list", "Items", [], 0) : new TableNode([], []) { SelectionId = "items/list" };
        var mappedTable = adapter.Node(table);
        var mappedAction = Assert.IsType<ActionNode>(adapter.Node(new ActionNode("restore", "Restore") { SelectionTableId = "items/list" }));
        Assert.Equal("profile:items%2Flist", mappedAction.SelectionTableId);
        Assert.Equal(mappedAction.SelectionTableId, queryable ? Assert.IsType<QueryableTableNode>(mappedTable).CollectionId : Assert.IsType<TableNode>(mappedTable).SelectionId);
        Assert.Null(Assert.IsType<ActionNode>(adapter.Node(new ActionNode("all", "All"))).SelectionTableId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexResolvesForwardReferencesAndReportsUnknownIdsWithoutThrowing(bool queryable)
    {
        var index = new ViewIndex();
        index.Add(new ActionNode("restore", "Restore") { SelectionTableId = "items" });
        Assert.Contains("items", Assert.Single(index.SelectionDiagnostics), StringComparison.Ordinal);
        index.Add(queryable ? new QueryableTableNode("items", "Items", [], 0) : new TableNode([], []) { SelectionId = "items" });
        Assert.Empty(index.SelectionDiagnostics);
        index.Clear();
        index.Add(new ActionNode("all", "All"));
        Assert.Empty(index.SelectionDiagnostics);
    }

    [Fact]
    public void DuplicateTableIdsHaveAnUnambiguousDiagnostic()
    {
        var index = new ViewIndex();
        index.Add(new SectionNode("Root", [new TableNode([], []) { SelectionId = "items" },
            new QueryableTableNode("items", "Items", [], 0), new ActionNode("restore", "Restore") { SelectionTableId = "items" }]));
        Assert.Contains(index.SelectionDiagnostics, diagnostic => diagnostic.Contains("ambiguous", StringComparison.Ordinal));
    }
}
