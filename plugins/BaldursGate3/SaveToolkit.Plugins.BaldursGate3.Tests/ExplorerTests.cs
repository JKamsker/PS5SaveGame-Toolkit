using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class ExplorerTests
{
    private const int LargeCount = 20_000;

    [Fact]
    public void HierarchicalPagesReachEveryEntryAndNeverExceedThePageLimit()
    {
        var entries = Enumerable.Range(0, LargeCount).Select(i => new DocumentEntry($"entry{i}.bin", [], null)).ToArray();
        var explorer = new ResourceExplorer(entries, NoFields);
        var pending = new Queue<ViewNode>(explorer.EntryList(null));
        var rows = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var node))
        {
            if (node is SectionNode { LazyId: { } id })
            {
                var children = explorer.Expand(id, null);
                Assert.InRange(children.Count, 1, ResourceExplorer.PageSize);
                foreach (var child in children)
                {
                    pending.Enqueue(child);
                }
            }
            else if (node is TableNode table)
            {
                Assert.InRange(table.Rows.Count, 1, ResourceExplorer.PageSize);
                Assert.All(table.Rows, row => Assert.True(rows.Add(row.Key)));
            }
        }

        Assert.Equal(LargeCount, rows.Count);
        Assert.Contains((LargeCount - 1).ToString(), rows);
    }

    [Fact]
    public void LargeChildListsHaveStableNodeIdsAndAllChildrenRemainReachable()
    {
        var nodes = Enumerable.Range(0, LargeCount).Select(i => new ResourceNode($"node{i}", i == 0 ? -1 : 0)).ToArray();
        nodes[0].Children.AddRange(Enumerable.Range(1, LargeCount - 1));
        var resource = new LarianResource("synthetic", nodes, [], [0]);
        var explorer = new ResourceExplorer([new DocumentEntry("large.lsf", [], resource)], NoFields);
        var pending = new Queue<ViewNode>(explorer.Expand("node/0/0", null));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var node))
        {
            if (node is not SectionNode { LazyId: { } id })
            {
                continue;
            }

            Assert.True(seen.Add(id));
            if (id.StartsWith("node/", StringComparison.Ordinal))
            {
                continue;
            }

            var children = explorer.Expand(id, null);
            Assert.InRange(children.Count, 1, ResourceExplorer.PageSize);
            foreach (var child in children)
            {
                pending.Enqueue(child);
            }
        }

        Assert.Equal(LargeCount - 1, seen.Count(id => id.StartsWith("node/", StringComparison.Ordinal)));
        Assert.Contains("node/0/19999", seen);
        Assert.IsType<TextNode>(explorer.Expand("node/0/19999", null)[0]);
    }

    [Fact]
    public void PrimitiveEditsAreBoundAndNoOpIsByteIdentical()
    {
        var file = MemorySaveSet.SingleFile("test.lsf", ResourceFixture.Create(), "PPSA18463");
        using var document = new BaldursGate3Plugin().Open(file);
        var fields = Walk(document).OfType<FieldNode>().ToArray();
        Assert.Equal(3, fields.Length);
        Assert.All(fields, field =>
        {
            Assert.False(field.Spec.ReadOnly);
            Assert.Equal(Confidence.Unknown, field.Confidence);
            Assert.Equal(field.Value, document.GetValue(field.ValueId));
        });
        var counter = Assert.Single(fields, field => field.Label.StartsWith("Counter", StringComparison.Ordinal));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(document.ApplyEdits([new ValueEdit(counter.ValueId, FieldValue.Of(99L))]).Succeeded);
        Assert.Equal(99, document.GetValue(counter.ValueId).AsInt32());
        Assert.True(document.CanEdit);
    }

    [Fact]
    public void BlobRangesAndThumbnailsReadTheActualPayload()
    {
        byte[] webp = [.. "RIFF"u8, 4, 0, 0, 0, .. "WEBP"u8];
        var bytes = PackageFixture.Create(payloads: [("image.webp", webp), ("data.bin", [1, 2, 3])]);
        using var document = new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile("test.lsv", bytes));
        var image = Assert.Single(Walk(document).OfType<ImageNode>());
        var asset = Assert.IsType<AssetContent>(document.GetAsset(image.AssetId));
        Assert.Equal("image/webp", asset.ContentType);
        Assert.Equal(webp, asset.Content.ToArray());
        Assert.Equal(new byte[] { 2, 3 }, document.ReadBlob("payload/1", 1, 99).ToArray());
        Assert.Empty(document.ReadBlob("original", bytes.Length, 10).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("original", -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("original", 0, -1));
        Assert.Throws<KeyNotFoundException>(() => document.ReadBlob("unknown", 0, 1));
        Assert.Null(document.GetAsset("image/1"));
    }

    private static IEnumerable<ViewNode> Walk(ISaveDocument document)
    {
        var pending = new Queue<ViewNode>();
        pending.Enqueue(document.BuildView());
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var node))
        {
            yield return node;
            IEnumerable<ViewNode> children = node switch
            {
                TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children),
                SectionNode { LazyId: { } id } when expanded.Add(id) => document.ExpandNode(id),
                SectionNode { LazyId: null } section => section.Children,
                TableNode table => table.Rows.SelectMany(row => row.Cells.Values),
                _ => [],
            };
            foreach (var child in children)
            {
                pending.Enqueue(child);
            }
        }
    }

    private static FieldNode NoFields(string id, string label, ResourceAttribute attribute) =>
        throw new InvalidOperationException("This fixture has no primitive fields.");
}
