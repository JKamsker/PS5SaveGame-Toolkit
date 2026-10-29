using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroQueryCharacterizationTests
{
    private const string CollectionId = "astro.collectibles";
    private const string Artifact = "/PreInstall[0]/Artifact[0]/";
    private const string Puzzle = "/PreInstall[0]/Puzzle[0]/beta[0]";
    private const string Root = "\r\n<PreInstall><Artifact>" +
        "<zeta><IsGot>false</IsGot></zeta><alpha><IsGot>false</IsGot></alpha>" +
        "<Alpha><IsGot>true</IsGot></Alpha><empty/><unknown><IsGot>0</IsGot></unknown>" +
        "</Artifact><Puzzle><beta><IsGot>false</IsGot></beta></Puzzle></PreInstall>";
    private static readonly string[] KeyOrder =
        [Artifact + "Alpha[0]", Artifact + "alpha[0]", Artifact + "empty[0]", Artifact + "unknown[0]", Artifact + "zeta[0]", Puzzle];

    [Theory]
    [InlineData(null)]
    [InlineData("key")]
    [InlineData("name")]
    public void DefaultAndNameSortAreCaseInsensitiveWithAscendingOrdinalTies(string? column)
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes(Root));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.Equal(KeyOrder, Keys(queryable, new() { SortColumn = column }));
        Assert.Equal(new[] { Puzzle, Artifact + "zeta[0]", Artifact + "unknown[0]", Artifact + "empty[0]",
            Artifact + "Alpha[0]", Artifact + "alpha[0]" }, Keys(queryable, new() { SortColumn = column, Descending = true }));
        Assert.Equal(KeyOrder.Skip(1).Take(3), Keys(queryable, new() { SortColumn = column, Offset = 1, Limit = 3 }));
        Assert.Equal(AstroTestData.Bytes(Root), AstroTestData.Output(document));
    }

    [Theory]
    [InlineData("category")]
    [InlineData("world")]
    [InlineData("image")]
    [InlineData("details")]
    public void EqualProjectedValuesKeepAscendingNativeKeysInBothDirections(string column)
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes(Root));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.Equal(KeyOrder, Keys(queryable, new() { SortColumn = column }));
        Assert.Equal(KeyOrder, Keys(queryable, new() { SortColumn = column, Descending = true }));
    }

    [Fact]
    public void StatusSortAndFiltersUseSavedFlagsAndDoNotTrimFilterValues()
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes(Root));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.Equal(new[] { Artifact + "alpha[0]", Artifact + "zeta[0]", Puzzle, Artifact + "Alpha[0]",
            Artifact + "empty[0]", Artifact + "unknown[0]" }, Keys(queryable, new() { SortColumn = "status" }));
        Assert.Equal(new[] { Artifact + "empty[0]", Artifact + "unknown[0]", Artifact + "Alpha[0]",
            Artifact + "alpha[0]", Artifact + "zeta[0]", Puzzle }, Keys(queryable, new() { SortColumn = "status", Descending = true }));
        var page = queryable.Query(CollectionId, new()
        {
            Text = "  ARTIFACT/  ", Offset = 1, Limit = 1,
            Filters = new Dictionary<string, string> { ["status"] = "mIsSiNg", ["world"] = "uNmApPeD" }
        });
        Assert.Equal(6, page.TotalCount);
        Assert.Equal(2, page.FilteredCount);
        Assert.Equal(Artifact + "zeta[0]", Assert.Single(page.Rows).Key);
        Assert.Equal(1, page.Offset);
        Assert.Equal(1, page.Limit);
        foreach (var value in new[] { " Missing ", "", " " })
            Assert.Empty(Keys(queryable, new() { Filters = new Dictionary<string, string> { ["status"] = value } }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    [InlineData("  raw KEY retained.  ")]
    [InlineData(" NO IDENTIFIED IMAGE ")]
    [InlineData("unmapped")]
    public void SearchUsesEveryCheapProjectionAndNormalizesOnlySearchWhitespace(string? text)
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes(Root));
        Assert.Equal(KeyOrder, Keys(Assert.IsAssignableFrom<IQueryableDocument>(document), new() { Text = text }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyRecordsAreUnknownOwnershipAndNeverHiddenAsEmptySlots(bool? includeEmpty)
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes(Root));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.All(queryable.Collections, collection => Assert.Null(collection.EmptyRows));
        var page = queryable.Query(CollectionId, new() { IncludeEmpty = includeEmpty });
        Assert.Equal(KeyOrder, page.Rows.Select(row => row.Key));
        Assert.Equal(6, page.TotalCount);
        Assert.Equal(6, page.FilteredCount);
        Assert.Equal(0, page.HiddenEmptyCount);
        Assert.Null(page.NextCursor);
        Assert.All(page.Rows, row => Assert.False(row.IsEmpty));
        Assert.Equal("Unknown", Assert.IsType<TextNode>(page.Rows[2].Cells["status"]).Text);
        var emptyGroup = queryable.Query(CollectionId + ".Prop", new() { IncludeEmpty = includeEmpty });
        Assert.Empty(emptyGroup.Rows);
        Assert.Equal(0, emptyGroup.TotalCount);
        Assert.Equal(0, emptyGroup.FilteredCount);
        Assert.Equal(0, emptyGroup.HiddenEmptyCount);
        var pastEnd = queryable.Query(CollectionId, new() { Offset = int.MaxValue, IncludeEmpty = includeEmpty });
        Assert.Empty(pastEnd.Rows);
        Assert.Equal(6, pastEnd.FilteredCount);
    }

    [Fact]
    public void GroupCountsAndDuplicateSavedNamesKeepTheirNativeOccurrenceKeys()
    {
        const string text = "\r\n<PreInstall><Artifact><same/><same/><same/></Artifact></PreInstall>";
        using var document = AstroTestData.Open(AstroTestData.Bytes(text));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.Equal(new long[] { 3, 3, 0, 0 }, queryable.Collections.Select(collection => collection.TotalCount));
        Assert.Equal(new[] { Artifact + "same[0]", Artifact + "same[1]", Artifact + "same[2]" },
            Keys(queryable, new() { SortColumn = "name", Descending = true }));
        Assert.Equal(3, queryable.Query(CollectionId + ".Artifact", new()).TotalCount);
    }

    [Fact]
    public void InvalidQueryDiagnosticsAndLimitsRemainPluginSpecific()
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes(Root));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        foreach (var query in new QueryableCollectionQuery[]
        {
            new() { Offset = -1 }, new() { Limit = 0 }, new() { Limit = 201 }, new() { Cursor = "" },
            new() { Text = new string(' ', 257) },
            new() { Filters = Enumerable.Range(0, 33).ToDictionary(index => index.ToString(), _ => "") }
        })
            Assert.Equal("Unsupported ASTRO query bounds or cursor.",
                Assert.Throws<ArgumentException>(() => queryable.Query(CollectionId, query)).Message);
        foreach (var query in new QueryableCollectionQuery[]
        {
            new() { SortColumn = "KEY" }, new() { SortColumn = "" },
            new() { Filters = new Dictionary<string, string> { ["foreign"] = "" } },
            new() { Filters = new Dictionary<string, string> { ["key"] = new string('x', 257) } }
        })
            Assert.Equal("Unknown ASTRO query column or oversized filter.",
                Assert.Throws<ArgumentException>(() => queryable.Query(CollectionId, query)).Message);
        Assert.Equal("Unknown ASTRO collection.", Assert.Throws<KeyNotFoundException>(() =>
            queryable.Query("foreign", new() { Limit = 0 })).Message);
        Assert.Equal(KeyOrder, Keys(queryable, new() { Limit = 200, Text = new string(' ', 256) }));
    }

    private static IEnumerable<string> Keys(IQueryableDocument document, QueryableCollectionQuery query) =>
        document.Query(CollectionId, query).Rows.Select(row => row.Key);
}
