using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

internal static class ViewTraversal
{
    private const int MaximumSectionChildren = 128;

    public static IReadOnlyList<FieldNode> Fields(ISaveDocument document, bool checkChildBound = false) =>
        Nodes(document.BuildView(), document.ExpandNode, checkChildBound).OfType<FieldNode>().ToArray();

    public static IReadOnlyList<ViewNode> Nodes(ISaveDocument document) => Nodes(document.BuildView(), document.ExpandNode);

    public static IReadOnlyList<ViewNode> Nodes(DocumentSession session) => Nodes(session.BuildView(), session.Expand);

    public static IReadOnlyList<ViewNode> Nodes(ViewNode root, Func<string, IReadOnlyList<ViewNode>>? expand = null, bool checkChildBound = false)
    {
        var nodes = new List<ViewNode>();
        Visit(root);
        return nodes;

        void Visit(ViewNode node)
        {
            nodes.Add(node);
            if (node is TabsNode tabs)
                foreach (var child in tabs.Tabs.SelectMany(tab => tab.Children)) Visit(child);
            if (node is SectionNode section)
            {
                var children = section.LazyId is { } id && expand is not null ? expand(id) : section.Children;
                if (checkChildBound) Assert.True(children.Count <= MaximumSectionChildren);
                foreach (var child in children) Visit(child);
            }
        }
    }
}
