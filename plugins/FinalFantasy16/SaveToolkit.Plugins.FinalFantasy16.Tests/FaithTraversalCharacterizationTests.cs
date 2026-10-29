using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithTraversalCharacterizationTests
{
    [Fact]
    public void DocumentFieldsRetainTabSectionRowAndCellOrder()
    {
        var first = Field("first");
        var cell = Field("cell");
        var nested = Field("nested");
        var last = Field("last");
        var action = new ActionNode("action", "Action") { Parameters = [Field("parameter")] };
        var root = new TabsNode([
            new Tab("First", [new SectionNode("Section", [first,
                new TableNode([], [new TableRow("row", new Dictionary<string, ViewNode>
                {
                    ["field"] = cell, ["nested"] = new SectionNode("Nested", [nested]), ["action"] = action,
                })]), SectionNode.Lazy("Unexpanded", "lazy")])]),
            new Tab("Last", [last]),
        ]);

        Assert.Equal(new[] { first, cell, nested, last }, FaithViewFields.All(root));
        Assert.Empty(FaithViewFields.All(action));
        Assert.Empty(FaithViewFields.All(SectionNode.Lazy("Unexpanded", "lazy")));
        Assert.Equal(new[] { cell }, FaithViewFields.All(cell));
        Assert.Equal(new[] { action }, FaithViewNodes.All(root).OfType<ActionNode>());
        Assert.Same(root, FaithViewNodes.All(root).First());
        Assert.Same(last, FaithViewNodes.All(root).Last());
    }

    private static FieldNode Field(string id) => new(id, id, IntegerSpec.Int32, FieldValue.Of(1));
}
