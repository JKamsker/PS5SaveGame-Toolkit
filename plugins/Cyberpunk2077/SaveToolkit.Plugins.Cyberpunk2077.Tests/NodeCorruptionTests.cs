using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class NodeCorruptionTests
{
    [Theory]
    [InlineData(0, -2)] // Next ID: invalid sentinel.
    [InlineData(0, 0)] // Self/cycle.
    [InlineData(0, 2)] // Out of bounds.
    [InlineData(4, -2)] // Child ID: invalid sentinel.
    [InlineData(4, 0)] // Self/cycle.
    [InlineData(4, 2)] // Skips first child.
    [InlineData(8, -1)]
    [InlineData(8, int.MinValue)]
    [InlineData(8, int.MaxValue)]
    [InlineData(8, 44)] // Before payload.
    [InlineData(8, 53)] // Beyond expanded payload.
    [InlineData(12, -1)]
    [InlineData(12, 0)]
    [InlineData(12, 3)] // Truncated node ID.
    [InlineData(12, int.MaxValue)]
    public void InvalidNodeLinksOffsetsAndSizesUseFormatErrors(int word, int value)
    {
        var bytes = CsavCorruptionFixture.Flat();
        var directory = BinaryData.Int32(bytes, bytes.Length - 8);
        const int firstNodeWords = 7; // EDON, packed count, packed name length, one name byte.
        CsavCorruptionFixture.SetWord(bytes, directory + firstNodeWords + word, value);
        Assert.Throws<SaveFormatException>(() => Open(bytes));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(100_001)]
    [InlineData(int.MaxValue)]
    public void InvalidNodeCountsCannotSizeAnAllocation(int count)
    {
        var valid = CsavCorruptionFixture.Flat();
        var directory = BinaryData.Int32(valid, valid.Length - 8);
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write(valid.AsSpan(0, directory + 4));
        CsavCorruptionFixture.WritePacked(writer, count);
        writer.Write(valid.AsSpan(directory + 5));
        var bytes = output.ToArray();
        Assert.Contains("node count", Assert.Throws<SaveFormatException>(() => Open(bytes)).Message);
    }

    [Fact]
    public void EveryDirectoryTruncationWithValidFooterIsRejected()
    {
        var valid = CsavCorruptionFixture.Flat();
        var directory = BinaryData.Int32(valid, valid.Length - 8);
        for (var end = directory + 4; end < valid.Length - 8; end++)
        {
            byte[] bytes = [.. valid.AsSpan(0, end), .. valid.AsSpan(valid.Length - 8)];
            Assert.Throws<SaveFormatException>(() => Open(bytes));
        }
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void NestingDepthHasAnExactBound(int depth, bool accepted)
    {
        var payload = new byte[(depth + 1) * sizeof(int)];
        var nodes = new FixtureNode[depth + 1];
        for (var i = 0; i <= depth; i++)
        {
            CsavCorruptionFixture.SetWord(payload, i * sizeof(int), i);
            nodes[i] = new("x", -1, i < depth ? i + 1 : -1, i * sizeof(int), payload.Length - i * sizeof(int));
        }
        var bytes = CsavCorruptionFixture.Create(payload, nodes);
        var container = CsavContainer.Parse(bytes);
        if (!accepted)
        {
            Assert.Contains("nesting", Assert.Throws<SaveFormatException>(() => CsavTree.Parse(container)).Message);
            return;
        }
        var tree = CsavTree.Parse(container);
        Assert.Equal(depth + 1, tree.Nodes.Count);
        Assert.Single(tree.Roots);
        Assert.Equal(bytes, container.Rebuild(container.Payload));
    }

    [Theory]
    [InlineData(100_000, true)]
    [InlineData(100_001, false)]
    public void NodeCountBoundIsCheckedEvenWhenAllDirectoryEntriesArePresent(int count, bool accepted)
    {
        var container = CsavContainer.Parse(CsavCorruptionFixture.Flat(count));
        if (accepted)
        {
            var tree = CsavTree.Parse(container);
            Assert.Equal(count, tree.Nodes.Count);
            Assert.Equal(count, tree.Roots.Count);
        }
        else Assert.Contains("node count", Assert.Throws<SaveFormatException>(() => CsavTree.Parse(container)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeterministicByteMutationsRejectCleanlyOrProduceConsistentTrees(bool compressed)
    {
        var valid = CsavCorruptionFixture.Flat(compressed: compressed);
        byte[] replacements = [0, 0x7F, 0x80, 0xFF];
        var accepted = 0;
        var rejected = 0;
        for (var offset = 0; offset < valid.Length; offset++)
        {
            foreach (var replacement in replacements)
            {
                var bytes = valid.ToArray();
                bytes[offset] = replacement;
                try
                {
                    var container = CsavContainer.Parse(bytes);
                    var tree = CsavTree.Parse(container);
                    foreach (var node in tree.Nodes)
                    {
                        Assert.Equal(node.ChildId, node.Children.Count == 0 ? -1 : node.Children[0]);
                        Assert.Equal(node.Id, BinaryData.Int32(container.Payload, node.Offset));
                    }
                    Assert.Equal(bytes, container.Rebuild(container.Payload));
                    accepted++;
                }
                catch (SaveFormatException error)
                {
                    Assert.False(string.IsNullOrWhiteSpace(error.Message));
                    rejected++;
                }
            }
        }
        Assert.True(accepted > 0);
        Assert.True(rejected > 0);
        Assert.Equal(valid.Length * replacements.Length, accepted + rejected);
    }

    private static void Open(byte[] bytes)
    {
        using var document = new Cyberpunk2077Plugin().Open(MemorySaveSet.SingleFile("sav.dat", bytes));
    }
}
