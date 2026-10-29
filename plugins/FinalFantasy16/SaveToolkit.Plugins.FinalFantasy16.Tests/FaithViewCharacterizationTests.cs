using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithViewCharacterizationTests
{
    // Golden protocol hashes refreshed for the #40 item-meaning badges and CRC evidence tooltip.
    // Project out the additive coverage actions and stored-stack state column to pin inherited content.
    [Theory]
    [InlineData("PPSA10665", false, "8fd78aff59875c710cce6df1c84886a329378a897f1562727273df79f196d8a1")]
    [InlineData("PPSA10665", true, "7e3ca31a09b611629995acfbd34c593ca558b44f7d9bdd09d2958eea32c265f0")]
    [InlineData("PPSA13803", false, "233d12a60799cc75e6c4cbd08119d211beff3198ed9036114c25d324fc11b996")]
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
        TableNode table => table with
        {
            Columns = table.Columns.Where(column => column.Key != FaithStoredStackState.ColumnKey).ToArray(),
            Rows = table.Rows.Select(row => row with
            {
                Cells = row.Cells.Where(pair => pair.Key != FaithStoredStackState.ColumnKey)
                    .ToDictionary(pair => pair.Key, pair => ExistingView(pair.Value)),
            }).ToArray(),
        },
        _ => node,
    };

    private static ViewNode[] ExistingChildren(IReadOnlyList<ViewNode> nodes) => nodes
        .Where(node => node is not ActionNode { ActionId: "set-gil" or "set-ap" or "set-renown" or "restore-imported-main-stacks" })
        .Select(ExistingView).ToArray();
}
