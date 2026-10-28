using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class ExplorerTests
{
    [Fact]
    public void CommonExplorer_ReachesEveryBodyWordAndReturnsConsistentBindings()
    {
        var bytes = ContainerTests.Common("PPSA01911");
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 0x3F800000); // f32=1
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1036), 0xFFFFFFFF); // f32=NaN, i32=-1
        using var document = new FinalFantasy7Plugin().Open(MemorySaveSet.SingleFile("ff7remakecommon", bytes, "PPSA01911"));
        _ = document.BuildView();
        var sections = new Queue<string>(document.ExpandNode("record/0").OfType<SectionNode>().Select(n => n.LazyId!));
        var fields = new List<FieldNode>();
        while (sections.TryDequeue(out var id))
        {
            var children = document.ExpandNode(id);
            Assert.InRange(children.Count, 1, 32);
            foreach (var section in children.OfType<SectionNode>())
            {
                sections.Enqueue(section.LazyId!);
            }

            foreach (var table in children.OfType<TableNode>())
            {
                Assert.InRange(table.Rows.Count, 1, 64);
                fields.AddRange(table.Rows.SelectMany(r => r.Cells.Values).OfType<FieldNode>());
            }
        }

        Assert.Equal(250, fields.Count);
        Assert.Equal(fields.Count, fields.Select(f => f.ValueId).Distinct().Count());
        Assert.All(fields, f =>
        {
            Assert.Equal(Confidence.Unknown, f.Confidence);
            Assert.True(f.Spec.ReadOnly);
            Assert.Equal(f.Value, document.GetValue(f.ValueId));
        });
        Assert.Equal(0xFFFFFFFFU, document.GetValue("raw/0/1036").AsUInt32());
        var first = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("words/0/40/4")));
        Assert.Equal("1", Assert.IsType<TextNode>(first.Rows[0].Cells["float"]).Text);
    }

    [Fact]
    public void ReadOnlyDocument_RejectsEditsAndDoesNotExposeMutableBackingBuffers()
    {
        var bytes = ContainerTests.Common("PPSA01911");
        using var document = new FinalFantasy7Plugin().Open(MemorySaveSet.SingleFile("ff7remakecommon", bytes, "PPSA01911"));
        _ = document.ExpandNode("record/0");
        var outcome = document.ApplyEdits([new("raw/0/4", FieldValue.Of(0L)), new("missing", FieldValue.Of(1L))]);
        Assert.False(outcome.Succeeded);
        Assert.Equal(2, outcome.Errors.Count);
        Assert.Empty(outcome.Changes);
        var exported = Assert.Single(document.Serialize()).Content;
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(exported, out var outputArray));
        outputArray.Array![0] = 0;
        var blob = document.ReadBlob("file", 0, 16);
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(blob, out var blobArray));
        blobArray.Array![0] = 0;
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void Explorer_ValidatesLazyIdsAndBlobBounds()
    {
        using var document = new FinalFantasy7Plugin().Open(MemorySaveSet.SingleFile("ff7remakecommon",
            ContainerTests.Common("PPSA01911"), "PPSA01911"));
        foreach (var id in new[] { "nope", "record/-1", "record/2", "record/2147483648", "records/1", "inventory/0",
            "words/0/0/16", "words/0/40/3", "words/0/40/2147483647", "words/1/40/4", "words/0/1040/4" })
        {
            Assert.Throws<KeyNotFoundException>(() => document.ExpandNode(id));
        }

        Assert.Throws<KeyNotFoundException>(() => document.ReadBlob("record/2", 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("file", -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("file", long.MaxValue, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("file", 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("file", 0, 65537));
        Assert.Equal(16, document.ReadBlob("record/1", 0, 100).Length);
        Assert.Empty(document.ReadBlob("file", 1056, 1).ToArray());
        Assert.Equal(6, document.ReadBlob("file", 1050, 100).Length);
    }

    [Theory]
    [InlineData("PPSA01911", 1, "Potion", Confidence.Verified)]
    [InlineData("PPSA08664", 10, "Gil", Confidence.Verified)]
    [InlineData("PPSA18936", 10, "Gil", Confidence.Inferred)]
    public void GameplayExplorer_IsBoundedAndUsesSeparateNameMaps(string title, int itemSlot, string name, Confidence confidence)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var file = SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == schema.Prefix + "000");
        var bytes = file.ReadAllBytes();
        var records = LinkedRecords.Parse(bytes, schema, common: false);
        using var document = new FinalFantasy7Plugin().Open(file);
        var view = Assert.IsType<TabsNode>(document.BuildView());
        Assert.True(NodeCount(view) < 2500, "Opening a file must not eagerly decode its bodies or gameplay tables.");
        var summary = view.Tabs[0].Children.OfType<SectionNode>().Single();
        Assert.All(summary.Children.OfType<FieldNode>(), f => Assert.Equal(confidence, f.Confidence));
        var inventory = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("inventory/0")));
        Assert.Equal(128, inventory.Rows.Count);
        Assert.Equal(name, Assert.IsType<TextNode>(inventory.Rows[itemSlot].Cells["name"]).Text);
        Assert.All(inventory.Rows.SelectMany(r => r.Cells.Values).OfType<FieldNode>()
            .Where(f => f.ValueId.EndsWith("/id", StringComparison.Ordinal)), f => Assert.True(f.Spec.ReadOnly));
        Assert.All(inventory.Rows.SelectMany(r => r.Cells.Values).OfType<FieldNode>()
            .Where(f => f.ValueId.EndsWith("/quantity", StringComparison.Ordinal)), f => Assert.Equal(title == "PPSA18936", f.Spec.ReadOnly));
        Assert.Equal(104, Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("materia/7"))).Rows.Count);
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("materia/8"));
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("inventory/16"));
        Assert.InRange(document.ExpandNode("records/0").Count, 1, 32);
        foreach (var record in records)
        {
            Assert.Equal(bytes.AsSpan(record.Offset, 16).ToArray(), document.ReadBlob($"record/{record.Index}", 0, 16).ToArray());
        }

        var end = records[0].Length - 4;
        var raw = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode($"words/0/{end}/4")));
        Assert.Single(raw.Rows);
        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end)),
            Assert.IsType<FieldNode>(raw.Rows[0].Cells["uint"]).Value.AsUInt32());
    }

    private static int NodeCount(ViewNode node) => 1 + (node switch
    {
        TabsNode tabs => tabs.Tabs.SelectMany(t => t.Children).Sum(NodeCount),
        SectionNode section => section.Children.Sum(NodeCount),
        TableNode table => table.Rows.SelectMany(r => r.Cells.Values).Sum(NodeCount),
        _ => 0,
    });
}
