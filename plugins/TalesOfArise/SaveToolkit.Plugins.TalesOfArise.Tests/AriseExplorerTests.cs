using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseExplorerTests
{
    [Fact]
    public void EveryPrimitivePage_IsBoundedReadOnlyAndConsistent_AndBlobPagesAreIsolated()
    {
        var file = AriseSampleTests.GameplayFiles().First();
        using var document = new TalesOfArisePlugin().Open(file);
        var root = Assert.IsType<TabsNode>(document.BuildView());
        Assert.Equal(3, root.Tabs.Count);
        var words = 0;
        for (var index = 0; index < 30; index++)
        {
            var children = document.ExpandNode($"section/{index}");
            Assert.True(children.Count <= 135);
            foreach (var page in children.OfType<SectionNode>())
            {
                var table = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode(page.LazyId!)));
                Assert.InRange(table.Rows.Count, 1, 256);
                foreach (var row in table.Rows)
                {
                    var field = Assert.IsType<FieldNode>(row.Cells["value"]);
                    Assert.True(field.Spec.ReadOnly);
                    Assert.Equal(Confidence.Unknown, field.Confidence);
                    Assert.Equal(field.Value, document.GetValue(field.ValueId));
                    words++;
                }
            }
        }

        Assert.Equal(138832, words); // Sum of the 30 independently measured section sizes / sizeof(uint).
        var before = Assert.Single(document.Serialize()).Content.ToArray();
        var pageBytes = document.ReadBlob("payload", 0, 32);
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(pageBytes, out var array));
        array.Array![array.Offset] ^= 0xFF;
        Assert.Equal(before, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal(16, document.ReadBlob("outer", 0, 16).Length);
        Assert.Empty(document.ReadBlob("outer", AriseFormat.PayloadOffset, 16).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("payload", -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("outer", 0, -1));
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("page/0/1"));
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("section/30"));
    }
}
