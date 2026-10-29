using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonTestViewsTests
{
    [Fact]
    public void WalkRetainsOrderDuplicatesAndLazyPrecedenceWithoutEnteringParametersOrTables()
    {
        var field = new FieldNode("value/1", "field", new TextSpec(), FieldValue.Of("raw"));
        var ignored = field with { ValueId = "ignored" };
        var action = new ActionNode("action", "action") { Parameters = [ignored] };
        var table = new TableNode([], [new("row", new Dictionary<string, ViewNode> { ["hidden"] = ignored })]);
        var lazy = new SectionNode("lazy", [ignored]) { LazyId = "lazy-id" };
        var structure = field with { ValueId = "structure/0" };
        var section = new SectionNode("section", [structure, field, lazy]);
        var root = new TabsNode([new("one", [section]), new("two", [action, table, field])]);
        var calls = new List<string>();
        IReadOnlyList<ViewNode> Expand(string id) { calls.Add(id); return [field, action]; }

        ViewNode[] expected = [root, section, structure, field, lazy, field, action, action, table, field];
        Assert.Equal(expected, JsonTestViews.Walk(root, Expand, maxChildren: 3));
        Assert.Equal("lazy-id", Assert.Single(calls));
        Assert.Equal(Enumerable.Repeat(field, 3), JsonTestViews.Fields(root, Expand));
    }

    [Fact]
    public void ChildBoundIsOptionalAndAppliesToExpandedAndEagerSections()
    {
        var eager = new SectionNode("eager", [new TextNode("one"), new TextNode("two")]);
        var lazy = SectionNode.Lazy("lazy", "id");
        IReadOnlyList<ViewNode> Expand(string _) => eager.Children;
        Assert.Equal(3, JsonTestViews.Walk(eager, Expand).Count);
        Assert.Equal(3, JsonTestViews.Walk(lazy, Expand).Count);
        Assert.Throws<Xunit.Sdk.InRangeException>(() => JsonTestViews.Walk(eager, Expand, maxChildren: 1));
        Assert.Throws<Xunit.Sdk.InRangeException>(() => JsonTestViews.Walk(lazy, Expand, maxChildren: 1));
    }
}
