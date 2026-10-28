using System.Globalization;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class PresentationTests
{
    [Theory]
    [InlineData("data000.bin", 9)]
    [InlineData("data00-1.bin", 2)]
    public void EachTabStartsWithOneExperimentalNoticeAndIntegrityDetailsStayInHint(string fileName, int tabCount)
    {
        var file = SampleEvidenceTests.Files().Single(f => f.FileName == fileName);
        using var document = new PragmataDocument(file.RelativePath, file.ReadAllBytes());
        var view = Assert.IsType<TabsNode>(document.BuildView());
        Assert.Equal(tabCount, view.Tabs.Count);
        foreach (var tab in view.Tabs)
        {
            var notice = Assert.IsType<TextNode>(tab.Children[0]);
            Assert.Equal(PragmataDocument.EditWarning, notice.Text);
            Assert.Equal(Tone.Warning, notice.Tone);
            var nodes = tab.Children.SelectMany(Flatten).ToArray();
            Assert.Single(nodes.OfType<TextNode>(), n => n.Text == PragmataDocument.EditWarning);
            Assert.All(nodes.OfType<FieldNode>(), field => Assert.NotEqual(PragmataDocument.EditWarning, field.Warning));
        }

        var stats = view.Tabs.Single(t => t.Title == "Overview").Children.OfType<StatsNode>().Single();
        var integrity = stats.Items.Single(s => s.Label == "Integrity");
        Assert.Equal("Verified", integrity.Value);
        Assert.Matches(@"^Murmur3 \+ \d+ CityHash64 blocks \+ embedded key/IV verified$", integrity.Hint!);
        if (document.Layout.Header is null) Assert.Equal("Pragmata · System data", document.Title);
        else
        {
            var inventory = view.Tabs.Single(t => t.Title == "Inventory");
            Assert.Contains(inventory.Children.OfType<TextNode>(), note => note.Tone == Tone.Warning && note.Text.Contains("legal stack limits", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void FieldsKeepCompactProvenanceAndConfidenceIncludingLazyRawFields()
    {
        using var document = EditingTests.Gameplay();
        var difficulty = document.Layout.Header!.Find(KnownHashes.Difficulty)!;
        var field = document.Field(difficulty);
        Assert.Equal($"{difficulty.Path} · u32 @0x{difficulty.Offset:x} · {HashNames.Provenance(difficulty.Hash)}", field.Description);
        Assert.Equal(Confidence.Verified, field.Confidence);
        Assert.Null(field.Warning);
        Assert.Equal(Confidence.Inferred, document.Field(document.Layout.Header.Find(KnownHashes.Playtime)!).Confidence);

        var view = Assert.IsType<TabsNode>(document.BuildView());
        var rawHeader = view.Tabs.Single(t => t.Title == "Raw").Children.OfType<SectionNode>()
            .Single(s => s.LazyId!.StartsWith($"tree/{document.Layout.Header.Path}/", StringComparison.Ordinal));
        var raw = document.ExpandNode(rawHeader.LazyId!).OfType<FieldNode>().Single(f => f.ValueId == $"raw/{difficulty.Path}");
        Assert.Equal(field.Description, raw.Description);
        Assert.Equal(field.Confidence, raw.Confidence);
        Assert.Null(raw.Warning);
    }

    [Fact]
    public void GameplayTitleShowsCurrentPlaytimeWithInvariantDecimalFormatting()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            using var document = EditingTests.Gameplay();
            Assert.Equal("Pragmata · Slot 0 · 25.7 h", document.Title);
            var playtime = document.Layout.Header!.Find(KnownHashes.Playtime)!;
            Assert.True(document.ApplyEdits([new(playtime.Path, FieldValue.Of(5_400_000_000UL))]).Succeeded);
            Assert.Equal("Pragmata · Slot 0 · 1.5 h", document.Title);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static IEnumerable<ViewNode> Flatten(ViewNode node)
    {
        yield return node;
        IEnumerable<ViewNode> children = node switch
        {
            SectionNode section => section.Children,
            TableNode table => table.Rows.SelectMany(row => row.Cells.Values),
            _ => [],
        };
        foreach (var child in children.SelectMany(Flatten)) yield return child;
    }
}
