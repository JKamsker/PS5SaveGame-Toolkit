using System.Buffers.Binary;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithExplorerTests
{
    [Fact]
    public void LargeBlockExplorationIsLazyBoundedAndCoversEveryWordExactlyOnce()
    {
        const int WordCount = 150000;
        var payload = new byte[WordCount * sizeof(uint)];
        for (var word = 0; word < WordCount; word++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(word * sizeof(uint)), (uint)word);
        }

        var bytes = FaithTestData.Container((99, 1, payload));
        var explorer = new FaithExplorer(bytes, FaithContainer.Parse(bytes));
        var pending = new Queue<string>();
        pending.Enqueue("words/0/0/150000");
        var words = new HashSet<int>();
        while (pending.TryDequeue(out var id))
        {
            var children = explorer.Expand(id);
            Assert.InRange(children.Count, 1, 64);
            foreach (var child in children)
            {
                if (child is SectionNode { LazyId: { } lazy })
                {
                    pending.Enqueue(lazy);
                }
                else
                {
                    var table = Assert.IsType<TableNode>(child);
                    Assert.InRange(table.Rows.Count, 1, 128);
                    foreach (var row in table.Rows)
                    {
                        var offset = int.Parse(row.Key, System.Globalization.CultureInfo.InvariantCulture);
                        Assert.True(words.Add(offset));
                        var value = Assert.IsType<TextNode>(row.Cells["uint"]);
                        Assert.Equal(((offset - 32) / sizeof(uint)).ToString(System.Globalization.CultureInfo.InvariantCulture), value.Text);
                    }
                }
            }
        }

        Assert.Equal(WordCount, words.Count);
    }

    [Fact]
    public void BlobAndLazyRequestsAreRangeChecked()
    {
        var bytes = FaithTestData.Container((99, 1, new byte[8]));
        var explorer = new FaithExplorer(bytes, FaithContainer.Parse(bytes));
        Assert.Equal(bytes, explorer.ReadBlob(FaithExplorer.FileBlobId, 0, bytes.Length).ToArray());
        Assert.Equal(new byte[8], explorer.ReadBlob("block/0", 0, 8).ToArray());
        Assert.Empty(explorer.ReadBlob("block/0", 8, 0).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => explorer.ReadBlob("block/0", 8, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => explorer.ReadBlob("block/0", -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => explorer.ReadBlob("block/0", long.MaxValue, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => explorer.ReadBlob(FaithExplorer.FileBlobId, 0, 65537));
        Assert.Throws<KeyNotFoundException>(() => explorer.ReadBlob("block/1", 0, 0));
        Assert.Throws<KeyNotFoundException>(() => explorer.Expand("words/0/1/2147483647"));
        Assert.Throws<KeyNotFoundException>(() => explorer.Expand("words/0/-1/1"));
        Assert.Throws<KeyNotFoundException>(() => explorer.Expand("block/1"));
    }
}
