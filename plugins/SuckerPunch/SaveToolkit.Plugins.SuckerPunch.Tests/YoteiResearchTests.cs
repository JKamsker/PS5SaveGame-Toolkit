using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing.Diff;
using static SaveToolkit.Plugins.SuckerPunch.Tests.YoteiTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class YoteiResearchTests
{
    [Fact]
    public void AdditiveHypothesisMatchesEveryNonemptyPayloadButDoesNotProveTailCoverage()
    {
        var nonempty = Files.Where(file => Word(file.ReadAllBytes(), 0) != 0).ToArray();
        Assert.True(nonempty.Length >= 2);
        foreach (var file in nonempty)
        {
            var bytes = file.ReadAllBytes();
            var end = Word(bytes, 8);
            Assert.Equal(0x150U, Word(bytes, 0));
            Assert.InRange(end, 16U, (uint)bytes.Length);
            Assert.Equal((int)end, bytes.AsSpan().LastIndexOfAnyExcept((byte)0) + 1);
            Assert.Equal(Word(bytes, 12), Checksum(bytes));
            Assert.True(bytes.AsSpan((int)end).IndexOfAnyExcept((byte)0) < 0);
            Assert.Equal(Checksum(bytes), unchecked((uint)bytes.Skip(16).Sum(value => (long)value) + end + 0x150U));
        }
    }

    [Fact]
    public void BothNamedInventoryLayoutsTraverseAllSlotsAndOnlyTheFirstContainsChangingCounts()
    {
        var values = new HashSet<uint>();
        foreach (var file in Files)
        {
            var bytes = file.ReadAllBytes();
            if (Word(bytes, 0) == 0) continue;
            var primary = InventoryRecords(bytes);
            var secondaryStart = primary[^1].Data + 21;
            var relative = bytes.AsSpan(secondaryStart).IndexOf("\x30\0\0\0\x0b\0\0\0ArrowNormal"u8);
            Assert.True(relative >= 0);
            var secondary = ReadTable(bytes, secondaryStart + relative, primary.Count, 16);
            Assert.Equal(primary.Select(row => row.Name), secondary.Select(row => row.Name));
            Assert.All(secondary, row => Assert.True(bytes.AsSpan(row.Data, 16).IndexOfAnyExcept((byte)0) < 0));
            foreach (var row in primary.Where(row => row.Name.StartsWith("Arrow", StringComparison.Ordinal) || row.Name.StartsWith("Ammo", StringComparison.Ordinal)))
                Assert.Equal(Word(bytes, row.Data + 5), Word(bytes, row.Data + 9));
            values.Add(Word(bytes, primary.Single(row => row.Name == "Mon").Data + 5));
        }
        Assert.True(values.Count > 1);
        Assert.Contains(120U, values);
        Assert.Contains(112099U, values);
    }

    [Fact]
    public void EverySampleKeepsReadOnlyHeadersAndByteIdenticalNoOpIncludingPlaceholders()
    {
        Assert.NotEmpty(Files);
        foreach (var file in Files)
        {
            var bytes = file.ReadAllBytes();
            using var doc = Open(file);
            var view = Assert.IsType<TabsNode>(doc.BuildView());
            var header = Assert.IsType<SectionNode>(view.Tabs[0].Children[1]);
            Assert.All(header.Children, node => Assert.True(Assert.IsType<FieldNode>(node).Spec.ReadOnly));
            Assert.False(doc.ApplyEdits([new("container/word1", FieldValue.Of(17L))]).Succeeded);
            if (Word(bytes, 0) == 0) Assert.False(doc.CanEdit);
            else Assert.Equal(Inventory(doc).Rows.Any(row => !Assert.IsType<FieldNode>(row.Cells["quantity"]).Spec.ReadOnly), doc.CanEdit);
            var output = Assert.Single(doc.Serialize());
            Assert.Equal(file.RelativePath, output.RelativePath);
            Assert.Equal(0, ByteDiffer.Compare(bytes, output.Content.ToArray()).ChangedBytes);
        }
    }
}
