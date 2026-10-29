using System.Buffers.Binary;
using System.Diagnostics;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class CyberpunkSampleTests(ITestOutputHelper output)
{
    [Fact]
    public void EverySnapshotAndBackupMatchesKnownContainerAndInventory()
    {
        var plugin = new Cyberpunk2077Plugin();
        var files = ProgressionSampleTests.Files();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            var container = CsavContainer.Parse(bytes);
            var tree = CsavTree.Parse(container);
            Assert.Equal(265, container.SaveVersion);
            Assert.Equal(2210, container.GameVersion);
            Assert.Equal(195, container.ArchiveVersion);
            Assert.Equal(22, container.Chunks.Count);
            Assert.Equal(5_607_178, container.Payload.Length);
            Assert.Equal(159, tree.Nodes.Count);
            Assert.Equal("GameSessionDesc", tree.Nodes[0].Name);
            Assert.Contains(tree.Nodes, node => node.Name == "ScriptableSystemsContainer");
            var items = InventoryReader.Read(container, tree);
            Assert.Equal(22, items.Count);
            var money = Assert.Single(items, item => item.TweakId == InventoryReader.MoneyTweakId);
            Assert.Equal(1UL, money.ContainerId);
            Assert.Equal(39, money.NodeId);
            Assert.Equal(300U, BinaryData.UInt32(container.Payload, money.QuantityOffset!.Value));
            var timer = Stopwatch.StartNew();
            using var document = plugin.Open(file);
            var view = document.BuildView();
            timer.Stop();
            output.WriteLine($"{file.FileName}: open + initial view {timer.Elapsed.TotalMilliseconds:F1} ms");
            Assert.True(document.CanEdit);
            Assert.IsType<TabsNode>(view);
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void MoneyEditChangesOnlyItsFourPayloadBytesAndPreservesAllOtherData()
    {
        var plugin = new Cyberpunk2077Plugin();
        foreach (var file in ProgressionSampleTests.Files())
        {
            var before = CsavContainer.Parse(file.ReadAllBytes());
            var tree = CsavTree.Parse(before);
            var money = Assert.Single(InventoryReader.Read(before, tree), item => item.TweakId == InventoryReader.MoneyTweakId);
            using var document = plugin.Open(file);
            var fields = ExpandAll(document);
            Assert.Equal(20, fields.Count(field => !field.Spec.ReadOnly));
            var field = Assert.Single(fields, item => item.ValueId == $"inventory/{money.NodeId}/quantity");
            Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(123456L))]).Succeeded);
            var bytes = Assert.Single(document.Serialize()).Content.ToArray();
            var after = CsavContainer.Parse(bytes);
            var expected = before.Payload.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(money.QuantityOffset!.Value), 123456);
            Assert.Equal(expected, after.Payload);
            Assert.Equal(before.Original.AsSpan(before.DirectoryOffset, before.Original.Length - before.DirectoryOffset - 8).ToArray(),
                after.Original.AsSpan(after.DirectoryOffset, after.Original.Length - after.DirectoryOffset - 8).ToArray());
            Assert.Equal(before.Chunks.Select(c => c.ExpandedSize), after.Chunks.Select(c => c.ExpandedSize));
            for (var index = 0; index < before.Chunks.Count; index++)
            {
                var oldChunk = before.Chunks[index];
                var newChunk = after.Chunks[index];
                if (money.QuantityOffset < oldChunk.PayloadOffset || money.QuantityOffset >= oldChunk.PayloadOffset + oldChunk.ExpandedSize)
                {
                    Assert.Equal(before.Original.AsSpan(oldChunk.Offset, oldChunk.StoredSize).ToArray(),
                        after.Original.AsSpan(newChunk.Offset, newChunk.StoredSize).ToArray());
                }
            }

            using var reopened = plugin.Open(MemorySaveSet.SingleFile("sav.dat", bytes, "PPSA04027"));
            _ = reopened.BuildView();
            Assert.Equal(FieldValue.Of(123456L), reopened.GetValue(field.ValueId));
            Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
            Assert.Equal(before.Original, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void EditBatchIsAtomicAndReadBlobsAreIsolated()
    {
        using var document = new Cyberpunk2077Plugin().Open(ProgressionSampleTests.Files()[0]);
        var field = Assert.Single(ExpandAll(document), field => field.ValueId == "inventory/39/quantity");
        var before = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(99L)), new("missing", FieldValue.Of(1L))]).Succeeded);
        Assert.Equal(field.Value, document.GetValue(field.ValueId));
        Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(-1L))]).Succeeded);
        Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of("oops"))]).Succeeded);
        Assert.Equal(before, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("payload", -1, 1));
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("node/-1"));
        var copy = document.ReadBlob("payload", 0, 16).ToArray();
        copy[0] ^= 0xFF;
        Assert.NotEqual(copy, document.ReadBlob("payload", 0, 16).ToArray());
    }

    [Theory]
    [InlineData(4)] // Save schema.
    [InlineData(8)] // Game version.
    [InlineData(21)] // Archive version.
    public void UnknownSchemaRetainsExplorerButDisablesEditing(int offset)
    {
        var bytes = ProgressionSampleTests.Files()[0].ReadAllBytes();
        bytes[offset] ^= 1;
        using var document = new Cyberpunk2077Plugin().Open(MemorySaveSet.SingleFile("sav.dat", bytes));
        Assert.False(document.CanEdit);
        Assert.Empty(ExpandAll(document)); // Unsupported inventory/progression exposes raw data instead of typed fields.
        var root = Assert.IsType<TabsNode>(document.BuildView());
        var raw = Assert.IsType<SectionNode>(root.Tabs.Single(tab => tab.Title == "Node tree").Children[0]);
        Assert.Contains(document.ExpandNode(raw.LazyId!), node => node is HexNode);
        var facts = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("facts", new() { Limit = 200 });
        Assert.Equal(200, facts.Rows.Count);
        Assert.All(facts.Rows, row =>
        {
            var field = Assert.IsType<FieldNode>(row.Cells["value"]);
            Assert.True(field.Spec.ReadOnly);
            Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(1L))]).Succeeded);
        });
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void ExcludesJsonAndCompanions()
    {
        var plugin = new Cyberpunk2077Plugin();
        var companions = SampleSaves.Load("PPSA04027").AllDiskFiles.Where(f => f.FileName != "sav.dat").ToArray();
        Assert.NotEmpty(companions);
        foreach (var file in companions)
        {
            Assert.False(plugin.Probe(file).IsMatch);
        }
    }

    // Walk eager/lazy views only. F8 rows must be queried explicitly by their owning test.
    internal static IReadOnlyList<FieldNode> ExpandAll(ISaveDocument document)
    {
        var fields = new Dictionary<string, FieldNode>(StringComparer.Ordinal);
        var pending = new Queue<ViewNode>();
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        pending.Enqueue(document.BuildView());
        while (pending.TryDequeue(out var node))
        {
            if (node is FieldNode field)
            {
                fields.TryAdd(field.ValueId, field);
                Assert.Equal(field.Value, document.GetValue(field.ValueId));
            }

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

        return fields.Values.ToArray();
    }
}
