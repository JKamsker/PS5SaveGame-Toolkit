using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class ViewTraversalTests
{
    [Fact]
    public void WalkPreservesTabSectionAndLazyPreorderWithoutVisitingParametersOrCollections()
    {
        var first = Field("first");
        var nested = Field("nested");
        var last = Field("last");
        var action = new ActionNode("action", "Action") { Parameters = [Field("parameter")] };
        var table = new QueryableTableNode("collection", "Rows", [], 1000);
        var inner = SectionNode.Lazy("Inner", "inner");
        var outer = new SectionNode("Outer", [Field("ignored")]) { LazyId = "outer" };
        var eager = new SectionNode("Eager", [outer]);
        var root = new TabsNode([new("First", [first, eager, action, table]), new("Second", [last])]);
        using var document = new TraversalDocument(root, new() { ["outer"] = [nested, inner], ["inner"] = [Field("deep")] });
        var nodes = StructuralTests.Nodes(document);
        Assert.Equal([root, first, eager, outer, nested, inner, document.Children["inner"][0], action, table, last], nodes);
        Assert.Equal(["outer", "inner"], document.Expansions);
        document.Expansions.Clear();
        Assert.Equal(["first", "nested", "deep", "last"], XorJsonTests.Fields(document).Select(field => field.ValueId));
        Assert.Equal(["outer", "inner"], document.Expansions);
    }

    [Theory]
    [InlineData(128, false)]
    [InlineData(129, false)]
    [InlineData(128, true)]
    [InlineData(129, true)]
    public void FieldWalkKeepsTheExpansionBoundWhileNodeWalkCanInspectOversizedSections(int count, bool lazy)
    {
        var children = Enumerable.Range(0, count).Select(index => (ViewNode)Field(index.ToString())).ToArray();
        var root = lazy ? SectionNode.Lazy("Rows", "rows") : new SectionNode("Rows", children);
        using var document = new TraversalDocument(root, new() { ["rows"] = children });
        Assert.Equal(count + 1, StructuralTests.Nodes(document).Count);
        if (count <= 128) Assert.Equal(count, XorJsonTests.Fields(document).Count);
        else Assert.Throws<Xunit.Sdk.TrueException>(() => XorJsonTests.Fields(document));
    }

    [Fact]
    public void ShallowWalkUsesPublishedChildrenWithoutExpandingLazySections()
    {
        var published = Field("published");
        var lazy = new SectionNode("Lazy", [published]) { LazyId = "not-expanded" };
        var root = new TabsNode([new("Tab", [lazy])]);
        Assert.Equal([root, lazy, published], ViewTraversal.Nodes(root));
    }

    private static FieldNode Field(string id) => new(id, id, new BoolSpec(), FieldValue.Of(false));

    private sealed class TraversalDocument(ViewNode root, Dictionary<string, IReadOnlyList<ViewNode>> children) : BoundSaveDocument
    {
        public Dictionary<string, IReadOnlyList<ViewNode>> Children { get; } = children;
        public List<string> Expansions { get; } = [];
        public override string Title => "Traversal fixture";
        public override ViewNode BuildView() => root;
        public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
        {
            Expansions.Add(lazyId);
            return Children[lazyId];
        }
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
