using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasLazyViewTests
{
    [Fact]
    public void ExpandingAParentDoesNotBindFieldsInsideUnopenedDescendants()
    {
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("nested.sav", GvasFixture.Nested()));
        document.BuildView();
        var root = Assert.Single(document.ExpandNode("tree").OfType<SectionNode>());
        var children = document.ExpandNode(root.LazyId!);
        var items = Assert.Single(children.OfType<SectionNode>(), node => node.Title.StartsWith("Items", StringComparison.Ordinal));
        const string caption = "property/0/0/0/0";
        Assert.Throws<KeyNotFoundException>(() => document.GetValue(caption));
        var firstItem = document.ExpandNode(items.LazyId!).OfType<SectionNode>().First();
        Assert.Throws<KeyNotFoundException>(() => document.GetValue(caption));
        document.ExpandNode(firstItem.LazyId!);
        Assert.Equal("Item0", document.GetValue(caption).AsString());
        Assert.Equal(GvasFixture.Nested(), Assert.Single(document.Serialize()).Content.ToArray());
    }
}
