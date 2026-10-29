using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithViewCharacterizationTests
{
    // Golden protocol hashes captured from ddc304b before the maintainability refactors.
    // Project out only the four additive coverage actions to keep pinning all inherited content.
    [Theory]
    [InlineData("PPSA10665", false, "3a6a7dcb7f4700e27d6bf422d42f594eb65b2c149fb52fb71ff45cda931ea820")]
    [InlineData("PPSA10665", true, "f7a447a39602f3be38dcfa6b43dd4d4ab13b4c5863ee70a6ee0a6cc74df58b01")]
    [InlineData("PPSA13803", false, "e6e6b63ca387f4a0eb02e44a03d02744190a00d943c28fb3cc16298fee9846a9")]
    public void CompleteViewPreservesPresentationAndWritableSpecs(string title, bool extended, string expected)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var bytes = FaithCharacterizationData.Combined(extended);
            using var document = new FinalFantasy16Plugin().Open(FaithTestData.File(bytes, title));
            var view = document.BuildView();
            var actions = FaithViewNodes.All(view).OfType<ActionNode>().ToArray();
            foreach (var id in new[] { "set-gil", "set-ap", "set-renown", "restore-imported-main-stacks" })
            {
                var action = actions.First(node => node.ActionId == id);
                Assert.True(action.SupportsPlanning);
                Assert.StartsWith("Not validated on console", action.Description);
            }
            var json = JsonSerializer.SerializeToUtf8Bytes(ExistingView(view));
            Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(json)));
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static ViewNode ExistingView(ViewNode node) => node switch
    {
        SectionNode { Title: "Selected main-bank rows" } section => section.Children[0],
        SectionNode section => section with { Children = ExistingChildren(section.Children) },
        TabsNode tabs => tabs with { Tabs = tabs.Tabs.Select(tab => tab with { Children = ExistingChildren(tab.Children) }).ToArray() },
        TableNode table => table with { Rows = table.Rows.Select(row => row with
            { Cells = row.Cells.ToDictionary(pair => pair.Key, pair => ExistingView(pair.Value)) }).ToArray() },
        _ => node,
    };

    private static ViewNode[] ExistingChildren(IReadOnlyList<ViewNode> nodes) => nodes
        .Where(node => node is not ActionNode { ActionId: "set-gil" or "set-ap" or "set-renown" or "restore-imported-main-stacks" })
        .Select(ExistingView).ToArray();
}
