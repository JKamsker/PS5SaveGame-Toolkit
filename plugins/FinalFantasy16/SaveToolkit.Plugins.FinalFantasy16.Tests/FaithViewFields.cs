using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

internal static class FaithViewFields
{
    public static IEnumerable<FieldNode> All(ViewNode root) => root switch
    {
        FieldNode field => [field],
        TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children).SelectMany(All),
        SectionNode section => section.Children.SelectMany(All),
        TableNode table => table.Rows.SelectMany(row => row.Cells.Values).SelectMany(All),
        _ => [],
    };
}
