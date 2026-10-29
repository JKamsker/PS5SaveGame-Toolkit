using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokTestSupportCharacterizationTests
{
    [Fact]
    public void LegacyFixturesAndMemoryFilesOwnIndependentBytes()
    {
        var first = RagnarokTestSupport.LegacyGameplay();
        var second = RagnarokTestSupport.LegacyGameplay();
        var file = RagnarokTestSupport.GameplayFile(first);
        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        first[0x51C] ^= 1;
        Assert.Equal(1234u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(second.AsSpan(0x51C)));
        Assert.Equal(second, file.ReadAllBytes());
        var output = file.ReadAllBytes();
        output[0x51C] ^= 1;
        Assert.Equal(second, file.ReadAllBytes());
        Assert.Equal("PPSA08330/sdimg_TEST/GOWRSAVE0", file.RelativePath);
    }

    [Fact]
    public void EagerTraversalKeepsOrderDuplicatesAndTableCellsWithoutExpandingLazyNodes()
    {
        var repeated = new TextNode("repeated");
        var lazy = SectionNode.Lazy("not expanded", "never");
        var table = new TableNode([new("first", "First"), new("second", "Second")],
            [new("row", new Dictionary<string, ViewNode> { ["first"] = repeated, ["second"] = lazy })]);
        var section = new SectionNode("section", [repeated, table]);
        var root = new TabsNode([new("first", [section]), new("second", [repeated])]);

        var nodes = RagnarokTestSupport.EagerNodes(root).ToArray();
        Assert.Collection(nodes,
            node => Assert.Same(root, node), node => Assert.Same(section, node),
            node => Assert.Same(repeated, node), node => Assert.Same(table, node),
            node => Assert.Same(repeated, node), node => Assert.Same(lazy, node),
            node => Assert.Same(repeated, node));
    }

    [Fact]
    public void PinnedReferenceContextsTrackUsageIndependently()
    {
        var plugin = new GodOfWarRagnarokPlugin();
        var first = RagnarokTestSupport.PinnedReferences(plugin);
        var second = RagnarokTestSupport.PinnedReferences(plugin);
        Assert.NotSame(first, second);
        Assert.All(first.Datasets(), dataset => Assert.False(dataset.Used));
        Assert.All(second.Datasets(), dataset => Assert.False(dataset.Used));
        var record = first.Catalog(RagnarokReferences.CatalogId)!.Find("9E5240D27406E85A")!;
        Assert.Equal("Hacksilver", record.Name);
        Assert.Contains(first.Datasets(), dataset => dataset.Used);
        Assert.All(second.Datasets(), dataset => Assert.False(dataset.Used));
        var other = second.Catalog(RagnarokReferences.CatalogId)!.Find(record.Key)!;
        Assert.NotSame(record, other);
        Assert.Equal(record.Name, other.Name);
        Assert.Equal(record.AttributionText, other.AttributionText);
    }
}
