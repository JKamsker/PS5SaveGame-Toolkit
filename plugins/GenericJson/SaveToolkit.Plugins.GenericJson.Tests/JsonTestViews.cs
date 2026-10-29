using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson.Tests;

/// <summary>Walk published tabs/sections, expanding lazy sections without entering action parameters or table details.</summary>
internal static class JsonTestViews
{
    public static IReadOnlyList<ViewNode> Walk(ViewNode root, Func<string, IReadOnlyList<ViewNode>> expand, int? maxChildren = null) =>
        Walk([root], expand, maxChildren);

    public static IReadOnlyList<ViewNode> Walk(IReadOnlyList<ViewNode> roots, Func<string, IReadOnlyList<ViewNode>> expand, int? maxChildren = null)
    {
        var result = new List<ViewNode>();
        foreach (var node in roots) Visit(node);
        return result;

        void Visit(ViewNode node)
        {
            result.Add(node);
            if (node is TabsNode tabs)
                foreach (var child in tabs.Tabs.SelectMany(tab => tab.Children)) Visit(child);
            if (node is not SectionNode section) return;
            var children = section.LazyId is { } id ? expand(id) : section.Children;
            if (maxChildren is { } bound) Assert.InRange(children.Count, 0, bound);
            foreach (var child in children) Visit(child);
        }
    }

    public static IReadOnlyList<FieldNode> Fields(ViewNode root, Func<string, IReadOnlyList<ViewNode>> expand, int? maxChildren = null) =>
        Walk(root, expand, maxChildren).OfType<FieldNode>()
            .Where(field => !field.ValueId.StartsWith("structure/", StringComparison.Ordinal)).ToArray();
}
