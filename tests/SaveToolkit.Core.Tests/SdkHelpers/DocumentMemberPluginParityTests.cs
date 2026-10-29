using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class DocumentMemberPluginParityTests
{
    [Theory]
    [InlineData("xorjson")]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void AdapterMatchesUnchangedPluginCopiesForViewsExpansionsAndPages(string kind)
    {
        using var fixture = new GroupAdapterPluginFixture(kind);
        var root = Assert.IsType<SectionNode>(fixture.Group.BuildView());
        foreach (var file in fixture.Input.Members)
        {
            using var local = fixture.Plugin.Open(file.File);
            var adapter = new DocumentMemberAdapter(file.Role, local, fixture.Gearbox ? GroupAdapterPolicies.Gearbox : GroupAdapterPolicies.XorJson);
            var title = fixture.Gearbox ? file.Role == "profile" ? "Shared profile / bank" : "Character progression / backpack" : file.Role;
            var originalCopy = Assert.Single(Assert.Single(root.Children.OfType<SectionNode>(), section => section.Title == title).Children);
            var localRoot = local.BuildView();
            Assert.Equal(Identities(originalCopy), Identities(adapter.Node(localRoot)));
            foreach (var section in Eager(localRoot).OfType<SectionNode>().Where(s => s.LazyId is not null && !s.LazyId.EndsWith("/raw", StringComparison.Ordinal)))
            {
                var id = adapter.Prefix(section.LazyId!);
                Assert.Equal(fixture.Group.ExpandNode(id).SelectMany(Identities), adapter.ExpandNode(id).SelectMany(Identities));
            }
            foreach (var collection in adapter.Collections)
            {
                var query = new QueryableCollectionQuery { Limit = 2 };
                var expected = ((IQueryableDocument)fixture.Group).Query(collection.Id, query);
                var actual = adapter.Query(collection.Id, query);
                Assert.Equal(expected.Rows.Select(row => row.Key), actual.Rows.Select(row => row.Key));
                Assert.Equal(expected.Rows.SelectMany(r => r.Cells.Values).SelectMany(Identities), actual.Rows.SelectMany(r => r.Cells.Values).SelectMany(Identities));
                Assert.Equal(expected.TotalCount, actual.TotalCount);
                Assert.Equal(expected.FilteredCount, actual.FilteredCount);
            }
        }
    }

    private static IEnumerable<ViewNode> Eager(ViewNode node)
    {
        yield return node;
        var children = node switch
        {
            SectionNode section => section.Children,
            TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children),
            TableNode table => table.Rows.SelectMany(row => row.Cells.Values),
            _ => [],
        };
        foreach (var child in children)
            foreach (var nested in Eager(child)) yield return nested;
    }

    private static IEnumerable<string> Identities(ViewNode root) => Eager(root).Select(node => node switch
    {
        FieldNode field => "field:" + field.ValueId,
        SectionNode section => "section:" + section.LazyId,
        ActionNode action => "action:" + action.ActionId + ":" + string.Join(',', action.Parameters.Select(p => p.ValueId)),
        HexNode hex => "blob:" + hex.BlobId,
        QueryableTableNode table => "collection:" + table.CollectionId,
        TableNode table => "rows:" + string.Join(',', table.Rows.Select(r => r.Key)),
        GraphNode graph => JsonSerializer.Serialize<ViewNode>(graph),
        MapNode map => JsonSerializer.Serialize<ViewNode>(map),
        _ => node.GetType().Name,
    });
}
