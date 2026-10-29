using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class CollectionQueryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultOrderAndEqualSortValuesUseAscendingOrdinalKeys(bool descending)
    {
        var collection = Create([Entry("row/2", "potion", "7"), Entry("row/10", "POTION", "7"), Entry("row/1", "Potion", "7")]);
        foreach (var column in new string?[] { null, "name", "number" })
        {
            var page = collection.Query(new() { SortColumn = column, Descending = descending });
            Assert.Equal(["row/1", "row/10", "row/2"], page.Rows.Select(row => row.Key));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntegerSortRetainsSignedInt64Precision(bool descending)
    {
        var collection = Create([Entry("max", "", "9223372036854775807"), Entry("ten", "", "10"),
            Entry("negative", "", "-2"), Entry("previous", "", "9223372036854775806"), Entry("two", "", "2")]);
        string[] expected = ["negative", "two", "ten", "previous", "max"];
        Assert.Equal(descending ? expected.Reverse() : expected,
            collection.Query(new() { SortColumn = "number", Descending = descending }).Rows.Select(row => row.Key));
    }

    [Fact]
    public void MissingAndNonNumericValuesKeepOrdinalFallbackAndEmptyTies()
    {
        var missing = new CollectionEntry("a-missing", new Dictionary<string, string> { ["name"] = "Absent number" });
        var collection = Create([Entry("c-overflow", "", "9223372036854775808"), Entry("d-text", "", "abc"),
            Entry("b-empty", "", ""), missing]);
        Assert.Equal(["a-missing", "b-empty", "c-overflow", "d-text"],
            collection.Query(new() { SortColumn = "number" }).Rows.Select(row => row.Key));
        Assert.Equal(["b-empty"], collection.Query(new()
        {
            Filters = new Dictionary<string, string> { ["number"] = "" },
        }).Rows.Select(row => row.Key));
        Assert.Equal(["a-missing", "b-empty", "c-overflow", "d-text"],
            collection.Query(new() { SortColumn = "details" }).Rows.Select(row => row.Key));
    }

    [Theory]
    [InlineData(null, 3)]
    [InlineData("", 3)]
    [InlineData(" \t ", 3)]
    [InlineData("PoTiOn", 2)]
    [InlineData(" potion ", 1)]
    [InlineData("2", 1)]
    [InlineData("render-only", 0)]
    public void SearchUsesCheapValuesWithoutTrimmingNonblankText(string? text, int count)
    {
        var collection = Create([Entry("a", "Potion", "1"), Entry("b", " Potion ", "2"), Entry("c", "Ether", "3")]);
        var page = collection.Query(new() { Text = text });
        Assert.Equal(count, page.FilteredCount);
        Assert.Equal(count, page.Rows.Count);
        Assert.Equal(3, page.TotalCount);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public void FiltersUseExactCaseInsensitiveValuesAndOrdinalColumnNames()
    {
        var collection = Create([Entry("a", "Potion", "1"), Entry("b", " Potion ", "2"), Entry("c", "Ether", "3")]);
        Assert.Equal("a", Assert.Single(collection.Query(new()
        {
            Text = "OT", Filters = new Dictionary<string, string> { ["name"] = "pOTion" },
        }).Rows).Key);
        Assert.Empty(collection.Query(new() { Filters = new Dictionary<string, string> { ["name"] = "pot" } }).Rows);
        Assert.Empty(collection.Query(new() { Filters = new Dictionary<string, string> { ["details"] = "" } }).Rows);
    }

    [Fact]
    public void OnlyRequestedPageRendersAndEachQueryReadsFreshEntries()
    {
        var renders = 0;
        CollectionEntry[] entries = [Entry("c", "Potion", "3"), Entry("a", "Potion", "1"), Entry("b", "Potion", "2")];
        var collection = new CollectionQuery(Metadata(), () => entries.Select(entry => entry with
        {
            Render = () => { renders++; return new(entry.Key, new Dictionary<string, ViewNode>()); },
        }));
        Assert.Equal("b", Assert.Single(collection.Query(new() { Offset = 1, Limit = 1 }).Rows).Key);
        Assert.Equal(1, renders);
        Assert.Empty(collection.Query(new() { Offset = int.MaxValue }).Rows);
        Assert.Equal(1, renders);
        entries = [Entry("new", "Ether", "4")];
        Assert.Equal("new", Assert.Single(collection.Query(new()).Rows).Key);
        Assert.Equal(2, renders);
    }

    [Fact]
    public void ViewPreservesDescriptionsEmptyPresentationAndNativeEmptyFlag()
    {
        var metadata = Metadata() with { EmptyRows = EmptySlots.Presentation, Description = "Exact source caveat." };
        var collection = new CollectionQuery(metadata, () => [Entry("empty", "Empty slot", "0") with { IsEmpty = true }]);
        Assert.Equal(new QueryableTableNode(metadata.Id, metadata.Title, metadata.Columns, metadata.TotalCount)
        {
            EmptyRows = metadata.EmptyRows, Description = metadata.Description,
        }, collection.View());
        Assert.True(Assert.Single(collection.Query(new() { IncludeEmpty = true }).Rows).IsEmpty);
        Assert.Equal(1, collection.Query(new()).HiddenEmptyCount);
    }

    public static TheoryData<QueryableCollectionQuery, string> InvalidQueries => new()
    {
        { new() { Offset = -1 }, "Use bounded offset paging for FFVII collections." },
        { new() { Limit = 0 }, "Use bounded offset paging for FFVII collections." },
        { new() { Limit = QueryableCollectionLimits.MaxPageSize + 1 }, "Use bounded offset paging for FFVII collections." },
        { new() { Cursor = "" }, "Use bounded offset paging for FFVII collections." },
        { new() { Text = new('x', QueryableCollectionLimits.MaxQueryLength + 1) }, "Use bounded offset paging for FFVII collections." },
        { new() { Filters = Enumerable.Range(0, QueryableCollectionLimits.MaxFilterCount + 1).ToDictionary(i => $"key{i}", _ => "") }, "Use bounded offset paging for FFVII collections." },
        { new() { SortColumn = "Name" }, "Unknown FFVII collection column or oversized filter." },
        { new() { Filters = new Dictionary<string, string> { ["Name"] = "Potion" } }, "Unknown FFVII collection column or oversized filter." },
        { new() { Filters = new Dictionary<string, string> { ["name"] = new('x', QueryableCollectionLimits.MaxFilterLength + 1) } }, "Unknown FFVII collection column or oversized filter." },
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public void InvalidQueriesRetainExactDiagnostics(QueryableCollectionQuery query, string message) =>
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => Create([]).Query(query)).Message);

    private static QueryableCollection Metadata() => new("test", "Test rows",
        [new("name", "Name"), new("number", "Number", IntegerSpec.Int64), new("details", "Details")], 3);

    private static CollectionQuery Create(CollectionEntry[] entries) => new(Metadata(), () => entries);

    private static CollectionEntry Entry(string key, string name, string number) => new(key,
        new Dictionary<string, string> { ["name"] = name, ["number"] = number }, () => new(key,
            new Dictionary<string, ViewNode> { ["details"] = new TextNode("render-only") }));
}
