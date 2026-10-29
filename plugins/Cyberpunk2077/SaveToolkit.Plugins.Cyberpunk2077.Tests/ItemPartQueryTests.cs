using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class ItemPartQueryTests
{
    [Fact]
    public void LargePartTreeUsesOneSearchableCollectionWithEveryOriginalPath()
    {
        var additional = new ItemAdditional(0, 0, 0);
        var children = Enumerable.Range(0, 140).Select(index => new ItemPart(new(0, 0, 0, 0),
            $"Part {index:D3}", 0, 0, additional, [])).ToArray();
        var root = new ItemPart(new(0, 0, 0, 0), "Root", 0, 0, additional, children);
        var parts = new ItemPartInspection(54, root, new CyberpunkReferences(IReferenceContext.Empty, 2210));
        Assert.Equal(141, parts.View().TotalCount);
        Assert.Null(parts.View().EmptyRows);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset < 141; offset += 25)
        {
            var page = parts.Query(new() { Offset = offset, Limit = 25, IncludeEmpty = false });
            Assert.InRange(page.Rows.Count, 1, 25);
            Assert.Equal((141, 141, 0), (page.TotalCount, page.FilteredCount, page.HiddenEmptyCount));
            foreach (var row in page.Rows)
            {
                Assert.True(keys.Add(row.Key));
                Assert.False(row.IsEmpty); // A zero part ID has no confirmed vacant-slot meaning.
            }
        }
        Assert.Equal(ExtendedItemReader.Parts(root).Select(pair => $"item-parts/54/{pair.Path}").Order(), keys.Order());
        var found = Assert.Single(parts.Query(new() { Text = "Part 139" }).Rows);
        Assert.Equal("item-parts/54/root/139", found.Key);
        var exact = Assert.Single(parts.Query(new() { Filters = new Dictionary<string, string> { ["path"] = "root/128" } }).Rows);
        Assert.Equal("item-parts/54/root/128", exact.Key);
        Assert.Equal("Unknown part record; raw identity is preserved exactly.", Assert.IsType<SectionNode>(found.Cells["item"]).Description);
        Assert.Contains("WolvenKit", parts.Metadata.Description);
    }

    [Fact]
    public void SampleAttachmentExpansionPublishesOneF8TableWithOriginalKeysAndBytes()
    {
        var file = ProgressionSampleTests.Files()[0];
        var plugin = new Cyberpunk2077Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("part-tree", (DiskSaveFile)file, plugin, document);
        _ = session.BuildView();
        var inventory = Assert.IsType<TableNode>(Assert.Single(session.Expand("inventory/0/0")));
        Assert.Equal(22, inventory.Rows.Count);
        var shell = Assert.IsType<QueryableTableNode>(Assert.Single(session.Expand("item-parts/54/0")));
        Assert.Equal("item-parts/54", shell.CollectionId);
        Assert.Equal(4, shell.TotalCount);
        Assert.Contains(session.QueryableCollections, collection => collection.Id == shell.CollectionId);
        Assert.Equal(shell, Assert.Single(session.Expand("item-parts/54/0")));
        var page = session.QueryCollection(shell.CollectionId, new() { Limit = 2 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        var next = session.QueryCollection(shell.CollectionId, new() { Offset = 2, Limit = 2 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["item-parts/54/root", "item-parts/54/root/0", "item-parts/54/root/1", "item-parts/54/root/2"],
            page.Rows.Concat(next.Rows).Select(row => row.Key));
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("inventory/0/100"));
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("item-parts/54/128"));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(session.State.IsDirty);
    }
}
