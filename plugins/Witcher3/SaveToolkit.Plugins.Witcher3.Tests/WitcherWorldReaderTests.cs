using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherWorldReaderTests
{
    [Fact]
    public void FactsRetainWideAndContinuedNamesOpaqueWordsAndEveryEntryBit()
    {
        var raw = FactsFixture();
        var facts = FactsReader.ReadArchive(raw);
        Assert.Equal(2, facts.Count);
        Assert.Equal(new string('x', 65), facts[0].Name);
        Assert.Equal(0xABCD, facts[0].OpaqueWord);
        Assert.Equal(new FactValue(-2, 0x7FC00001, 0xBF800000), Assert.Single(facts[0].Entries));
        Assert.Equal("żółw", facts[1].Name);
        Assert.Empty(facts[1].Entries);
        Assert.Empty(FactsReader.ReadArchive(EmptyFacts()));
        Assert.Throws<SaveFormatException>(() => FactsReader.ReadArchive(raw.AsSpan()[..^1]));
        Assert.Throws<SaveFormatException>(() => FactsReader.ReadArchive([.. raw, 0]));
        // A bounded but invalid wide name must not discard every other fact or fabricate text.
        var opaque = FactsFixture();
        opaque[106] = 0;
        opaque[107] = 0xD8;
        var unnamed = FactsReader.ReadArchive(opaque)[1];
        Assert.True(unnamed.OpaqueName);
        Assert.StartsWith("opaque-name/1/", unnamed.Name, StringComparison.Ordinal);
        Assert.Equal(8, unnamed.NameBytes.Length);
    }

    [Theory]
    [InlineData(0)] // SXAP
    [InlineData(16)] // SBDF
    [InlineData(20)] // Fact count
    [InlineData(93)] // Entry count
    public void FactsRejectCorruptCountsAndMarkers(int offset)
    {
        var raw = FactsFixture();
        raw[offset] = 0xFF;
        Assert.Throws<SaveFormatException>(() => FactsReader.ReadArchive(raw));
    }

    [Fact]
    public void RecipeArraysRejectTrailingBytesDuplicateAndInvalidNames()
    {
        string[] names = ["Recipe A", "Diagram B"];
        byte[] valid = [2, 0, 0, 0, 1, 0, 2, 0];
        Assert.Equal(names, RecipeReader.ReadNames(valid, names));
        Assert.Empty(RecipeReader.ReadNames([0, 0, 0, 0], names));
        Assert.Throws<SaveFormatException>(() => RecipeReader.ReadNames([.. valid, 0], names));
        Assert.Throws<SaveFormatException>(() => RecipeReader.ReadNames([2, 0, 0, 0, 1, 0, 1, 0], names));
        Assert.Throws<SaveFormatException>(() => RecipeReader.ReadNames([1, 0, 0, 0, 0, 0], names));
        Assert.Throws<SaveFormatException>(() => RecipeReader.ReadNames([1, 0, 0, 0, 3, 0], names));
        Assert.Throws<SaveFormatException>(() => RecipeReader.ReadNames([255, 255, 255, 127], names));
    }

    [Theory]
    [InlineData(new byte[] { 0x81, 0xFF })] // Invalid UTF-8.
    [InlineData(new byte[] { 1, 0, 0xD8 })] // Unpaired UTF-16 surrogate.
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })] // Excessive compact length.
    [InlineData(new byte[] { 0x82, 65 })] // Truncated payload.
    public void SavedStringsRejectInvalidEncodingsAndLengths(byte[] raw)
    {
        Assert.Throws<SaveFormatException>(() => { var position = 0; return SavedString.Read(raw, ref position); });
    }

    [Fact]
    public void EverySampleMatchesIndependentRecipeJournalAndFactsCountsWithBoundedViews()
    {
        foreach (var file in SampleSaves.Load("PPSA04018").AllDiskFiles.Where(f => f.FileName.EndsWith(".sav", StringComparison.Ordinal)))
        {
            var (container, directory, tree, player) = Parse(file.ReadAllBytes());
            var data = new SavedWorldData(player, tree, container.Payload, directory.Names);
            Assert.Empty(data.Warnings);
            Assert.Equal(54, data.Recipes!.Count(r => r.Kind == "craftingSchematics"));
            Assert.Equal(14, data.Recipes!.Count(r => r.Kind == "alchemyRecipes"));
            var (facts, journal) = file.FileName switch {
                var n when n.Contains("313c5b3", StringComparison.Ordinal) => (429, 170),
                var n when n.Contains("317a3e7", StringComparison.Ordinal) => (611, 181),
                var n when n.Contains("31c45eb", StringComparison.Ordinal) => (614, 192),
                _ => (617, 198),
            };
            Assert.Equal(facts, data.Facts!.Count);
            Assert.Equal(facts == 429 ? 0 : 183, data.Facts.Count(f => f.OpaqueWord != 0));
            Assert.Contains(data.Facts, f => f.OpaqueName);
            Assert.Contains(data.Journal!, e => e.Path.Any(link => link.Resource.Length == 0));
            Assert.Equal(journal, data.Journal!.Count);
            Assert.Equal(journal, data.Journal.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count());
            Assert.All(data.Journal, entry => { Assert.NotEmpty(entry.Path); Assert.False(string.IsNullOrWhiteSpace(entry.Status)); });
            using var document = new Witcher3Plugin().Open(file);
            _ = document.BuildView();
            var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
            foreach (var (id, expected) in new[] { ("recipes", 68), ("facts", facts), ("journal", journal) })
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                for (var offset = 0; offset < expected; offset += 200)
                {
                    var page = queryable.Query("saved/" + id, new QueryableCollectionQuery { Offset = offset, Limit = 200 });
                    Assert.Equal(expected, page.TotalCount);
                    Assert.InRange(page.Rows.Count, 1, 200);
                    Assert.All(page.Rows, row => {
                        Assert.True(keys.Add(row.Key));
                        if (row.Cells["details"] is SectionNode { LazyId: { } lazy }) { Assert.NotEmpty(document.ExpandNode(lazy)); }
                    });
                }

                Assert.Equal(expected, keys.Count);
            }

            var exact = queryable.Query("saved/facts", new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["id"] = data.Facts[0].Name } });
            Assert.Equal(data.Facts[0].Name, Assert.Single(exact.Rows).Key);
            foreach (var column in exact.Columns)
            {
                Assert.Equal(facts, queryable.Query("saved/facts", new QueryableCollectionQuery { SortColumn = column.Key,
                    Filters = new Dictionary<string, string> { [column.Key] = "" } }).FilteredCount);
            }
            Assert.Throws<ArgumentException>(() => queryable.Query("saved/journal", new QueryableCollectionQuery { Cursor = "bad" }));
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void CorruptOptionalReadersFailIndependentlyAndJournalRejectsMalformedLinks()
    {
        var file = SampleSaves.Load("PPSA04018").AllDiskFiles.First(f => f.FileName.EndsWith(".sav", StringComparison.Ordinal));
        var (container, directory, tree, player) = Parse(file.ReadAllBytes());
        var facts = tree.Nodes.Single(n => n.Tag == "BS" && n.Name == "facts");
        var stream = tree.Nodes[Assert.Single(facts.Children)];
        var corrupt = container.Payload.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(corrupt.AsSpan(stream.Offset + 6 + 20), int.MaxValue);
        var data = new SavedWorldData(player, tree, corrupt, directory.Names);
        Assert.Null(data.Facts);
        Assert.NotNull(data.Journal);
        Assert.NotNull(data.Recipes);
        Assert.Single(data.Warnings);

        var entry = tree.Nodes.First(n => n.Name == "SJournalEntryStatus");
        var status = tree.Nodes[entry.Children.Single(id => tree.Nodes[id].Name == "status")];
        corrupt = container.Payload.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(corrupt.AsSpan(status.ValueOffset), 0);
        Assert.Throws<SaveFormatException>(() => JournalReader.Read(tree, corrupt, directory.Names));
        var path = tree.Nodes[entry.Children.Single(id => tree.Nodes[id].Name == "path")];
        var resource = tree.Nodes[path.Children.First(id => tree.Nodes[id].Name == "resource")];
        corrupt = container.Payload.ToArray();
        corrupt[resource.ValueOffset] = 0xFF;
        Assert.Throws<SaveFormatException>(() => JournalReader.Read(tree, corrupt, directory.Names));
        var unread = tree.Nodes[entry.Children.Single(id => tree.Nodes[id].Name == "JEntryUnread")];
        corrupt = container.Payload.ToArray();
        corrupt[unread.ValueOffset] = 2;
        Assert.Throws<SaveFormatException>(() => JournalReader.Read(tree, corrupt, directory.Names));
    }

    private static (WitcherContainer, Sav3Directory, Sav3Tree, PlayerData) Parse(byte[] bytes)
    {
        var container = WitcherContainer.Parse(bytes);
        var directory = Sav3Directory.Parse(container);
        var tree = Sav3Tree.Parse(container.Payload, directory);
        return (container, directory, tree, PlayerData.Read(container, directory, tree)!);
    }

    private static byte[] EmptyFacts() => [.. "SXAP"u8.ToArray(), .. new byte[12], .. "SBDF"u8.ToArray(), 0, 0, 0, 0, .. "EBDF"u8.ToArray()];

    private static byte[] FactsFixture()
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true);
        writer.Write("SXAP"u8);
        writer.Write(new byte[12]);
        writer.Write("SBDF"u8);
        writer.Write(2);
        writer.Write(new byte[] { 0xC1, 1 }); // 65 narrow bytes; compact continuation.
        writer.Write(Encoding.ASCII.GetBytes(new string('x', 65)));
        writer.Write((ushort)0xABCD);
        writer.Write((ushort)1);
        writer.Write((short)-2);
        writer.Write(0x7FC00001U); // Preserve NaN payload bits, do not normalize as a float.
        writer.Write(0xBF800000U);
        writer.Write((byte)4); // Four wide characters.
        writer.Write(Encoding.Unicode.GetBytes("żółw"));
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write("EBDF"u8);
        return buffer.ToArray();
    }
}
