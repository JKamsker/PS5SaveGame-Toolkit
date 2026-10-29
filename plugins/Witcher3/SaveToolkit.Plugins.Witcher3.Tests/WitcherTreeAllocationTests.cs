using System.Buffers.Binary;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherTreeAllocationTests
{
    [Fact]
    public void IndexedScalarTreeDoesNotAllocateDiscardedFallbackNamesAndTagStrings()
    {
        const int nodeCount = 2_000;
        const int tokenSize = 10;
        const int maximumBytesPerNode = 180;
        var data = new byte[nodeCount * tokenSize];
        var entries = new Sav3Entry[nodeCount];
        for (var index = 0; index < nodeCount; index++)
        {
            var offset = index * tokenSize;
            "VL"u8.CopyTo(data.AsSpan(offset));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 4), 2);
            entries[index] = new Sav3Entry(offset, tokenSize);
        }
        var directory = new Sav3Directory(["value", "Int32"], entries, data.Length);
        _ = Sav3Tree.Parse(data, directory); // Exclude initialization/JIT from the allocation budget.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var tree = Sav3Tree.Parse(data, directory);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(nodeCount, tree.Roots.Count);
        Assert.All(tree.Nodes, node => {
            Assert.Equal("VL", node.Tag);
            Assert.Equal("value", node.Name);
            Assert.Equal("Int32", node.Type);
            Assert.Equal(node.Id * tokenSize, node.Offset);
            Assert.Equal(tokenSize, node.Size);
            Assert.Equal(node.Offset + 6, node.ValueOffset);
            Assert.Equal(sizeof(int), node.ValueSize);
            Assert.Empty(node.Children);
        });
        Assert.True(allocated <= nodeCount * maximumBytesPerNode,
            $"Tree allocated {allocated:N0} bytes; budget {nodeCount * maximumBytesPerNode:N0}.");
    }
}
