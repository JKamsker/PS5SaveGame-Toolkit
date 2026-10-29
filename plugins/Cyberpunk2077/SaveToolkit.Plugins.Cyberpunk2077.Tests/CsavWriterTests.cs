using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class CsavWriterTests
{
    [Theory]
    [InlineData(false, -2)]
    [InlineData(false, 3)]
    [InlineData(true, -2)]
    [InlineData(true, 3)]
    public void ResizingNestedLeafAcrossChunksRetainsPatchesAndRepairsAncestorAndFollowingOffsets(bool compressed, int growth)
    {
        var payload = Enumerable.Repeat((byte)0xCC, 32).ToArray();
        int[] offsets = [0, 4, 16, 24];
        for (var id = 0; id < offsets.Length; id++) CsavCorruptionFixture.SetWord(payload, offsets[id], id);
        FixtureNode[] nodes = [new("parent", 3, 1, 0, 24), new("leaf", 2, -1, 4, 12),
            new("sibling", -1, -1, 16, 8), new("root", -1, -1, 24, 8)];
        var bytes = CsavCorruptionFixture.Create(payload, nodes, [8, 12, 12], compressed);
        var container = CsavContainer.Parse(bytes);
        Assert.Equal(bytes, container.Rebuild(container.Payload));
        var edited = payload.ToArray();
        edited[^1] = 0x5A;
        var replacement = Enumerable.Repeat((byte)0xAB, 12 + growth).ToArray();
        CsavCorruptionFixture.SetWord(replacement, 0, 1);
        byte[] expected = [.. edited.AsSpan(0, 4), .. replacement, .. edited.AsSpan(16)];

        var rebuilt = CsavContainer.Parse(container.Rebuild(edited, new(1, replacement)));

        Assert.Equal(payload, container.Payload);
        Assert.Equal(expected, rebuilt.Payload);
        Assert.Equal([8, 12 + growth, 12], rebuilt.Chunks.Select(chunk => chunk.ExpandedSize));
        Assert.Equal(bytes.AsSpan(container.Chunks[0].Offset, container.Chunks[0].StoredSize).ToArray(),
            rebuilt.Original.AsSpan(rebuilt.Chunks[0].Offset, rebuilt.Chunks[0].StoredSize).ToArray());
        var tree = CsavTree.Parse(rebuilt);
        Assert.Equal([0, 3], tree.Roots);
        Assert.Equal([1, 2], tree.Nodes[0].Children);
        Assert.Equal(new[] { (0, 24 + growth), (4, 12 + growth), (16 + growth, 8), (24 + growth, 8) },
            tree.Nodes.Select(node => (node.Offset, node.Size)));
        Assert.Equal(nodes.Select(node => (node.Name, node.Next, node.Child)),
            tree.Nodes.Select(node => (node.Name, node.NextId, node.ChildId)));
        Assert.Equal(rebuilt.Original, rebuilt.Rebuild(rebuilt.Payload));
    }

    [Theory]
    [InlineData(0, 4, 0)] // A parent cannot be replaced, even with its matching ID.
    [InlineData(1, 3, 1)] // A replacement must include the complete node ID.
    [InlineData(1, 4, 2)] // A leaf replacement must retain its ID.
    public void InvalidLeafReplacementsKeepTheExactDiagnostic(int nodeId, int length, int replacementId)
    {
        byte[] payload = [0, 0, 0, 0, 1, 0, 0, 0];
        var container = CsavContainer.Parse(CsavCorruptionFixture.Create(payload,
            [new("parent", -1, 1, 0, 8), new("leaf", -1, -1, 4, 4)]));
        var replacement = new byte[length];
        if (length >= sizeof(int)) CsavCorruptionFixture.SetWord(replacement, 0, replacementId);

        var error = Assert.Throws<SaveFormatException>(() => container.Rebuild(payload, new(nodeId, replacement)));

        Assert.Equal("Only complete leaf-node replacements with matching IDs are supported.", error.Message);
        Assert.Equal(payload, container.Payload);
    }
}
