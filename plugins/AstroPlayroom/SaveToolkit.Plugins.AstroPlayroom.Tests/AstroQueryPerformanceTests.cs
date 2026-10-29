using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroQueryPerformanceTests
{
    private const string Collection = "astro.collectibles";
    private static readonly string[] MissingOrder = ["a[0]", "b[0]"];
    private static readonly string[] StatusOrder = ["A[0]", "a[0]", "b[0]"];

    [Fact]
    public void RepeatedSearchDoesNotRebuildEveryRecordsStringsOrAdapters()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        using var document = plugin.Open(AstroTestData.Samples()[0], context);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var query = new QueryableCollectionQuery { Text = "nonexistentneedle", Limit = 20 };
        var allocated = AstroPerformanceData.Allocated(() => _ = queryable.Query(Collection, query));
        Assert.InRange(allocated, 0, 80_000);
        Assert.Empty(queryable.Query(Collection, query).Rows);
    }

    [Fact]
    public void CompleteModelSearchAllocationStaysBoundedWithoutPrivateSamples()
    {
        var bytes = AstroTestData.Bytes("\r\n<PreInstall><Artifact>" +
            string.Concat(Enumerable.Range(0, 1500).Select(i => $"<item{i}><IsGot>false</IsGot></item{i}>")) +
            "</Artifact></PreInstall>");
        using var document = AstroTestData.Open(bytes);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var query = new QueryableCollectionQuery { Text = "nonexistentneedle", Limit = 1 };
        var allocated = AstroPerformanceData.Allocated(() => _ = queryable.Query(Collection, query));
        Assert.InRange(allocated, 0, 450_000);
        Assert.Equal(1500, queryable.Query(Collection, query).TotalCount);
        Assert.Empty(queryable.Query(Collection, query).Rows);
    }

    [Fact]
    public void CachedQueriesReadLiveStatusAcrossEditRevertAndSeparateDocuments()
    {
        const string root = "\r\n<PreInstall><Artifact><b><IsGot>false</IsGot></b>" +
            "<A><IsGot>true</IsGot></A><a><IsGot>false</IsGot></a></Artifact></PreInstall>";
        using var document = AstroTestData.Open(AstroTestData.Bytes(root));
        using var other = AstroTestData.Open(AstroTestData.Bytes(root));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var query = new QueryableCollectionQuery
        {
            SortColumn = "status", Filters = new Dictionary<string, string> { ["status"] = "Missing" }
        };
        var original = queryable.Query(Collection, query);
        Assert.Equal(MissingOrder, original.Rows.Select(row => row.Key.Split('/')[3]));
        var id = original.Rows[0].Key + "/IsGot[0]/text[0]";
        Assert.True(document.ApplyEdits([new ValueEdit(id, FieldValue.Of(true))]).Succeeded);
        Assert.Single(queryable.Query(Collection, query).Rows);
        Assert.Equal(2, queryable.Query(Collection + ".Artifact", new() { Text = "Owned" }).FilteredCount);
        var sorted = queryable.Query(Collection, new() { SortColumn = "status", Descending = true });
        Assert.Equal(StatusOrder, sorted.Rows.Select(row => row.Key.Split('/')[3]));
        Assert.Equal("Owned", Assert.IsType<TextNode>(sorted.Rows[1].Cells["status"]).Text);
        Assert.Equal(2, Assert.IsAssignableFrom<IQueryableDocument>(other).Query(Collection, query).Rows.Count);
        Assert.True(document.ApplyEdits([new ValueEdit(id, FieldValue.Of(false))]).Succeeded);
        Assert.Equal(original.Rows.Select(row => row.Key), queryable.Query(Collection, query).Rows.Select(row => row.Key));
        Assert.Equal(AstroTestData.Bytes(root), AstroTestData.Output(document));
    }
}
