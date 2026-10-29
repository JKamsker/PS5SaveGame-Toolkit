using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

internal static class FaithViewNodes
{
    // Inspect only materialized document nodes; action parameters are a separate input form.
    public static IEnumerable<ViewNode> All(ViewNode root) => new[] { root }.Concat(root switch
    {
        TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children).SelectMany(All),
        SectionNode section => section.Children.SelectMany(All),
        TableNode table => table.Rows.SelectMany(row => row.Cells.Values).SelectMany(All),
        _ => [],
    });
}
