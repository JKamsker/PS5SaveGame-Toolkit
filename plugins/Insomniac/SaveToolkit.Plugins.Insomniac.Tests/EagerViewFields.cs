using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac.Tests;

/// <summary>Fields already present under tabs/sections; never queries tables or expands nodes.</summary>
internal static class EagerViewFields
{
    internal static IEnumerable<FieldNode> Enumerate(ViewNode node) => node switch
    {
        FieldNode field => [field],
        TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children).SelectMany(Enumerate),
        SectionNode section => section.Children.SelectMany(Enumerate),
        _ => [],
    };
}
