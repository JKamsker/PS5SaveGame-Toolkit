using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokCollectionTests
{
    [Fact]
    public void QueriesFindUnloadedRowsFilterGloballyAndKeepStableIdentities()
    {
        var file = SampleSaves.Load("PPSA08330").AllDiskFiles.First(file => file.FileName == "GOWRSAVE0");
        var plugin = new GodOfWarRagnarokPlugin();
        using var document = plugin.Open(file, RagnarokTestSupport.PinnedReferences(plugin));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var root = RagnarokTestSupport.EagerNodes(document.BuildView()).OfType<QueryableTableNode>().ToArray();
        Assert.Equal(8, root.Length);
        Assert.Equal(798, Assert.Single(queryable.Collections, c => c.Id == "directory").TotalCount);

        var page = queryable.Query("resources", new() { Text = "Hacksilver" });
        Assert.True(page.TotalCount > 211);
        Assert.True(page.FilteredCount > 1);
        Assert.All(page.Rows, row => Assert.Contains("Hacksilver", Assert.IsType<TextNode>(row.Cells["name"]).Text));
        Assert.Equal(page.Rows.Count, page.Rows.Select(row => row.Key).Distinct().Count());
        var hero = queryable.Query("resources", new()
        {
            Filters = new Dictionary<string, string> { ["container"] = "Hero wallet (inferred)" },
            SortColumn = "quantity", Descending = true, Limit = 1000,
        });
        Assert.Equal(211, hero.FilteredCount);
        Assert.Equal(200, hero.Rows.Count);
        var quantities = hero.Rows.Select(row => Assert.IsType<FieldNode>(row.Cells["quantity"])).ToArray();
        Assert.True(quantities.Zip(quantities.Skip(1)).All(pair => pair.First.Value.AsUInt64() >= pair.Second.Value.AsUInt64()));
        Assert.All(quantities, field =>
        {
            Assert.False(field.Spec.ReadOnly);
            Assert.Contains("Integrity unknown", field.Warning);
            Assert.Equal(Confidence.Inferred, field.Confidence);
            Assert.Equal(field.Value, document.GetValue(field.ValueId));
            Assert.NotEmpty(field.Description!);
        });

        var directory = queryable.Query("directory", new() { Offset = 600, Limit = 1 });
        var id = Assert.IsType<TextNode>(Assert.Single(directory.Rows).Cells["id"]).Text;
        var found = queryable.Query("directory", new() { Text = id });
        Assert.Equal(1, found.FilteredCount);
        Assert.Equal(directory.Rows[0].Key, found.Rows[0].Key);
        Assert.Empty(queryable.Query("directory", new() { Offset = int.MaxValue }).Rows);
        Assert.Throws<ArgumentException>(() => queryable.Query("directory", new() { Cursor = "unsupported" }));
        Assert.Throws<ArgumentException>(() => queryable.Query("directory", new() { SortColumn = "missing" }));
        Assert.Throws<ArgumentException>(() => queryable.Query("directory", new() { Text = new string('x', 257) }));
        Assert.Throws<KeyNotFoundException>(() => queryable.Query("missing", new()));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void RavenMappingsAndUnresolvedProgressionAreVisibleWithoutClaimingCompletion()
    {
        var file = SampleSaves.Load("PPSA08330").AllDiskFiles.First(file => file.FileName == "GOWRSAVE0");
        using var document = new GodOfWarRagnarokPlugin().Open(file);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var collectibles = queryable.Query("collectibles", new());
        Assert.Equal(2, collectibles.Rows.Count);
        Assert.All(collectibles.Rows, row =>
        {
            Assert.Contains("GPL-3.0", Assert.IsType<TextNode>(row.Cells["source"]).Text);
            Assert.Contains("51e19b2a", Assert.IsType<TextNode>(row.Cells["source"]).Text);
        });
        Assert.Equal(132, queryable.Query("flags", new()).TotalCount);
        Assert.Equal(261, queryable.Query("nested-ids", new()).TotalCount);
        var xp = queryable.Query("xp", new()
        {
            Filters = new Dictionary<string, string> { ["container"] = "Hero wallet (inferred)" },
        }).Rows;
        Assert.Equal(3, xp.Count);
        var kratos = Assert.Single(xp, row => Assert.IsType<TextNode>(row.Cells["name"]).Text == "Kratos XP");
        Assert.Equal(84_725, Assert.IsType<FieldNode>(kratos.Cells["quantity"]).Value.AsInt32());
    }
}
