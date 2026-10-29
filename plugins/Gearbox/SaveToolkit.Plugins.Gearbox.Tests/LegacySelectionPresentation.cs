using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox.Tests;

/// <summary>
/// Reconstruct only the pre-selection-wiring presentation for the existing protocol fingerprints.
/// Current declarations are independently asserted by SelectionDeclarationTests. Keep all field,
/// row, plan, outcome and serialized-byte evidence in the historical transcript unchanged.
/// </summary>
internal static class LegacySelectionPresentation
{
    internal static ViewNode View(GearboxGroupDocument group)
    {
        var root = Assert.IsType<SectionNode>(group.BuildView());
        return root with
        {
            Children = root.Children.Select(node => node switch
            {
                ActionNode action => action with { SelectionTableId = null },
                SectionNode section when section.Title == "Character progression / backpack" => Member(section, "character", group.Character),
                SectionNode section when section.Title == "Shared profile / bank" => Member(section, "profile", group.Profile),
                _ => node,
            }).ToArray(),
        };
    }

    private static SectionNode Member(SectionNode section, string role, GearboxDocument document)
    {
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        var legacy = tabs with
        {
            Tabs = tabs.Tabs.Select(tab => tab.Title != "Inventory" ? tab : tab with
            {
                Children = tab.Children.SelectMany(node => node is TextNode { Text: GearboxItemActions.SelectionHint }
                    ? GearboxItemActions.SelectedActions() : new[] { node }).ToArray(),
            }).ToArray(),
        };
        // Reconstruct historical placement before applying the unchanged member identity policy.
        return section with { Children = [new DocumentMemberAdapter(role, document, GearboxGroupIdentity.Policy).Node(legacy)] };
    }

    internal static IEnumerable<ViewNode> Walk(ISaveDocument document, ViewNode node)
    {
        yield return node;
        var children = node switch
        {
            SectionNode section when section.LazyId?.Contains("/raw", StringComparison.Ordinal) == true => [],
            SectionNode section when section.LazyId is { } id => document.ExpandNode(id)
                .Where(child => !id.Contains("/detail/", StringComparison.Ordinal) || child is not ActionNode action ||
                    !(action.ActionId.EndsWith("/items/duplicate", StringComparison.Ordinal) || action.ActionId.EndsWith("/items/remove", StringComparison.Ordinal))).ToArray(),
            SectionNode section => section.Children,
            TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children).ToArray(),
            TableNode table => table.Rows.SelectMany(row => row.Cells.Values).ToArray(),
            _ => Array.Empty<ViewNode>(),
        };
        foreach (var child in children)
            foreach (var descendant in Walk(document, child)) yield return descendant;
    }
}
