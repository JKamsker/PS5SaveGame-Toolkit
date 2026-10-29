using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class DocumentMemberViewTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NestedViewsRespectBothPoliciesAndPreservePresentation(bool gearbox)
    {
        using var document = new MemberAdapterDocument { CanEdit = false };
        var member = new DocumentMemberAdapter("profile", document, gearbox ? GroupAdapterPolicies.Gearbox : GroupAdapterPolicies.XorJson);
        var field = Assert.IsType<FieldNode>(document.BuildView());
        var parameter = field with { ValueId = "value" };
        var action = new ActionNode("set", "Set") { Parameters = [parameter], Mutates = false, SupportsPlanning = true, Confirm = "confirm" };
        var nested = new TableNode([], [new("inner", new Dictionary<string, ViewNode> { ["field"] = field })]);
        var row = new TableRow("outer", new Dictionary<string, ViewNode> { ["nested"] = nested }) { IsEmpty = true };
        var table = new TableNode([], [row]) { EmptyRows = new("nested"), Title = "Eager", Description = "Description" };
        var tabs = new TabsNode([new("Tab", [table])]);
        var target = new ViewTarget(ViewTargetKind.Action, "set") { Path = ["section:lazy", "tab:Tab", "row:outer"] };
        var graph = new GraphNode("Graph", [new("one", "One", GraphState.Available) { Target = target },
            new("two", "Two", GraphState.Locked)], [new("one", "two")], "Graph text");
        var map = new MapNode("Map", new(0, 0, 1, 1), [new("one", "Marker", MapMarkerStatus.Unknown, "C") { Target = target }], "Map text");
        var image = new ImageNode("pack:asset", "Alt");
        var root = new SectionNode("Root", [tabs, SectionNode.Lazy("Lazy", "lazy"), action, new HexNode("blob", 1),
            new QueryableTableNode("items", "Items", document.Collections[0].Columns, 1), graph, map, image]);
        var originalJson = JsonSerializer.Serialize<ViewNode>(root);
        var mapped = Assert.IsType<SectionNode>(member.Node(root));
        var outer = Assert.Single(Assert.IsType<TableNode>(Assert.Single(Assert.Single(Assert.IsType<TabsNode>(mapped.Children[0]).Tabs).Children)).Rows);
        Assert.Equal("profile/view/0/0/0/outer", outer.Key);
        Assert.True(outer.IsEmpty);
        var inner = Assert.Single(Assert.IsType<TableNode>(outer.Cells["nested"]).Rows);
        Assert.Equal("profile/view/0/0/0/outer/" + (gearbox ? "nested/" : "") + "inner", inner.Key);
        Assert.Equal(member.Prefix(field.ValueId), Assert.IsType<FieldNode>(inner.Cells["field"]).ValueId);
        Assert.Equal("profile/lazy", Assert.IsType<SectionNode>(mapped.Children[1]).LazyId);
        var mappedAction = Assert.IsType<ActionNode>(mapped.Children[2]);
        Assert.Equal("profile/set", mappedAction.ActionId);
        Assert.Same(action.Parameters, mappedAction.Parameters);
        Assert.False(mappedAction.Mutates);
        Assert.True(mappedAction.SupportsPlanning);
        Assert.Equal("confirm", mappedAction.Confirm);
        Assert.Equal("profile/blob", Assert.IsType<HexNode>(mapped.Children[3]).BlobId);
        Assert.Equal("profile/items", Assert.IsType<QueryableTableNode>(mapped.Children[4]).CollectionId);
        var mappedGraph = Assert.IsType<GraphNode>(mapped.Children[5]);
        Assert.Equal(gearbox ? "profile/one" : "one", mappedGraph.Items[0].Id);
        Assert.Equal(gearbox ? new("profile/one", "profile/two") : new GraphEdge("one", "two"), Assert.Single(mappedGraph.Edges));
        var mappedTarget = mappedGraph.Items[0].Target!;
        Assert.Equal("profile/set", mappedTarget.Id);
        Assert.Equal(target.Kind, mappedTarget.Kind);
        string[] expectedPath = gearbox
            ? ["section:Gearbox character + profile", "section:Shared profile / bank", "section:profile/lazy", "tab:Tab", "row:outer"]
            : ["section:XorJson character + global", "section:profile", "section:lazy", "tab:Tab", "row:outer"];
        Assert.Equal(expectedPath, mappedTarget.Path);
        Assert.Null(mappedGraph.Items[1].Target);
        var marker = Assert.Single(Assert.IsType<MapNode>(mapped.Children[6]).Markers);
        Assert.Equal(gearbox ? "profile/one" : "one", marker.Id);
        Assert.Equal(mappedTarget.Id, marker.Target!.Id);
        Assert.Equal(mappedTarget.Path, marker.Target.Path);
        Assert.Same(image, mapped.Children[7]);
        var metadata = member.Collection(document.Collections[0]);
        Assert.Equal(!gearbox, metadata.Columns[0].Spec!.ReadOnly);
        Assert.False(document.Collections[0].Columns[0].Spec!.ReadOnly);
        Assert.Equal(originalJson, JsonSerializer.Serialize<ViewNode>(root));
        Assert.Equal("Eager", Assert.IsType<TableNode>(Assert.Single(Assert.Single(Assert.IsType<TabsNode>(mapped.Children[0]).Tabs).Children)).Title);
    }

    [Fact]
    public void CustomIdentityCodecAndScopeValidationAreExplicitCallbacks()
    {
        using var document = new MemberAdapterDocument();
        var calls = 0;
        var policy = new DocumentMemberIdentityPolicy
        {
            ToGroupId = static (role, id) => role + ":" + Uri.EscapeDataString(id),
            TryToLocalId = static (role, id) => id.StartsWith(role + ":", StringComparison.Ordinal) ? Uri.UnescapeDataString(id[(role.Length + 1)..]) : null,
            LocalScope = (member, scope) =>
            {
                Assert.Same(document, member.Document);
                calls++;
                return new(scope.RowKeys?.Select(key => member.Local(key).Split('/', 2)[1]).ToArray());
            },
        };
        var adapter = new DocumentMemberAdapter("a", document, policy);
        var id = adapter.Prefix(MemberAdapterDocument.ValueId);
        Assert.Contains("%2F", id, StringComparison.Ordinal);
        Assert.Equal(MemberAdapterDocument.ValueId, adapter.Local(id));
        var plan = adapter.PlanAction("a:set", new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of(2) }, new([adapter.Prefix("items/key")]));
        Assert.Equal(["key"], document.LastScope!.RowKeys);
        Assert.Equal(id, Assert.Single(plan.Changes).ValueId);
        Assert.Equal(1, calls);
    }
}
