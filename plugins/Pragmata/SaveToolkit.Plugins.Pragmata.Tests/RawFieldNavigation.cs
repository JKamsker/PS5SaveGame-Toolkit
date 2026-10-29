using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

internal static class RawFieldNavigation
{
    public static FieldNode Find(TabsNode view, SaveNode target, Func<string, IReadOnlyList<ViewNode>> expand)
    {
        IReadOnlyList<ViewNode> nodes = view.Tabs.Single(tab => tab.Title == "Raw").Children;
        var ancestors = new Stack<(SaveNode Node, int ChildIndex)>();
        for (var child = target; child.Parent is { } parent; child = parent)
            ancestors.Push((parent, child.Index >= 0 ? child.Index : parent.Children.ToList().IndexOf(child)));
        foreach (var (ancestor, index) in ancestors)
        {
            foreach (var part in LazyPages.NavigationPath($"tree/{ancestor.Path}", ancestor.Children.Count, index))
            {
                var section = nodes.OfType<SectionNode>().Single(section => $"section:{section.LazyId}" == part);
                nodes = expand(section.LazyId!);
                Assert.InRange(nodes.Count, 1, LazyPages.PageSize);
            }
        }
        return nodes.OfType<FieldNode>().Single(field => field.ValueId == $"raw/{target.Path}");
    }
}
