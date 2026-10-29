using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class RawFieldNavigationTests
{
    [Theory]
    [InlineData(1, 0, 2)]
    [InlineData(200, 199, 2)]
    [InlineData(201, 0, 3)]
    [InlineData(201, 200, 3)]
    [InlineData(40_001, 200, 4)]
    [InlineData(40_001, 40_000, 3)]
    public void FindsExactFieldAcrossEagerAndRecursiveRangeSections(int count, int index, int expectedExpansions)
    {
        var original = SyntheticInventory.Create(1);
        using var document = new PragmataDocument("data000.bin", original);
        var quantity = document.Layout.Items[0].Find(KnownHashes.Quantity)!;
        // An authored model avoids constructing/decrypting a huge container just to test navigation.
        var root = new SaveNode(0x12345678, FieldTags.Class);
        var array = new SaveNode(0x23456789, FieldTags.Array, root);
        root.Children = [array];
        array.Children = Enumerable.Range(0, count).Select(position =>
            new SaveNode(0, FieldTags.Int32, array, position) { Offset = quantity.Offset, Size = quantity.Size }).ToArray();
        var pages = new LazyPages();
        var raw = new RawExplorer(document, pages);
        var view = new TabsNode([new Tab("Raw", [raw.Node(root)])]);
        var calls = 0;
        IReadOnlyList<ViewNode> Expand(string id) { calls++; return pages.Expand(id); }

        var field = RawFieldNavigation.Find(view, array.Children[index], Expand);

        Assert.Equal($"raw/12345678/23456789[{index}]", field.ValueId);
        Assert.Equal(document.Read(quantity), field.Value);
        Assert.Equal(field.Value, document.GetValue(field.ValueId));
        Assert.Equal(expectedExpansions, calls);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }
}
