using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Api.Performance.Tests;

internal static class ReviewSampleViews
{
    public static IEnumerable<ViewNode> Walk(ISaveDocument document, ViewNode root) =>
        Walk(root, document.ExpandNode, new HashSet<string>(StringComparer.Ordinal));

    public static IEnumerable<ViewNode> Walk(ViewNode node, Func<string, IReadOnlyList<ViewNode>> expand, HashSet<string> expanded)
    {
        yield return node;
        IEnumerable<ViewNode> children = node switch
        {
            SectionNode { LazyId: { } lazy } when expanded.Add(lazy) => expand(lazy),
            SectionNode { LazyId: null } section => section.Children,
            TabsNode tabs => tabs.Tabs.SelectMany(t => t.Children),
            TableNode table => table.Rows.SelectMany(r => r.Cells.Values),
            _ => [],
        };
        foreach (var child in children)
            foreach (var descendant in Walk(child, expand, expanded)) yield return descendant;
    }
}
