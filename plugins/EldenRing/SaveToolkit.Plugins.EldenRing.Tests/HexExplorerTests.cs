using System.Runtime.InteropServices;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class HexExplorerTests
{
    [Fact]
    public void SlotHex_IsLazy_BoundedAndIsolated()
    {
        var fixture = new SyntheticSave([9]);
        using var document = fixture.Open();
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        Assert.All(tabs.Tabs.Single(tab => tab.Title == "Raw").Children, node => Assert.NotNull(Assert.IsType<SectionNode>(node).LazyId));
        var hex = Assert.IsType<HexNode>(Assert.Single(document.ExpandNode("raw/slots/9")));
        Assert.Equal(SyntheticSave.SlotSize, hex.Length);
        var first = document.ReadBlob(hex.BlobId, 0, sizeof(uint));
        Assert.True(fixture.Bytes.AsSpan(SyntheticSave.Slot(9), sizeof(uint)).SequenceEqual(first.Span));
        Assert.True(MemoryMarshal.TryGetArray(first, out var array));
        array.Array![array.Offset] ^= 1;
        Assert.True(fixture.Bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
        Assert.Single(document.ReadBlob(hex.BlobId, hex.Length - 1, 256).ToArray());
        Assert.True(document.ReadBlob(hex.BlobId, hex.Length, 256).IsEmpty);
    }

    [Theory]
    [InlineData(-1L, 1)]
    [InlineData(long.MaxValue, 1)]
    [InlineData(0L, -1)]
    [InlineData(0L, 65537)]
    public void InvalidPageRequests_AreRejected(long offset, int count)
    {
        using var document = new SyntheticSave([0]).Open();
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("raw/slots/0", offset, count));
    }

    [Fact]
    public void UnknownSectionsAndBlobs_AreRejected()
    {
        using var document = new SyntheticSave([0]).Open();
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("characters/10"));
        Assert.Throws<KeyNotFoundException>(() => document.ReadBlob("raw/slots/10", 0, 1));
    }
}
