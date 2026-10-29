using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class QueryMutationEffectivenessTests
{
    [Theory]
    [InlineData("id")]
    [InlineData("null")]
    [InlineData("limit-low")]
    [InlineData("limit-high")]
    [InlineData("offset")]
    [InlineData("cursor-empty")]
    [InlineData("cursor-long")]
    [InlineData("cursor-offset")]
    [InlineData("text")]
    [InlineData("filter-count")]
    [InlineData("filter-empty-key")]
    [InlineData("filter-key")]
    [InlineData("filter-value")]
    public void InvalidQueriesAreRejectedBeforeCallingTheProvider(string fault)
    {
        using var document = new QueryProbe();
        using var fixture = new MutationSessionFixture(document);
        var query = fault switch
        {
            "limit-low" => new QueryableCollectionQuery { Limit = 0 },
            "limit-high" => new() { Limit = QueryableCollectionLimits.MaxPageSize + 1 },
            "offset" => new() { Offset = -1 },
            "cursor-empty" => new() { Cursor = "" },
            "cursor-long" => new() { Cursor = new string('x', QueryableCollectionLimits.MaxCursorLength + 1) },
            "cursor-offset" => new() { Cursor = "cursor", Offset = 1 },
            "text" => new() { Text = new string('x', QueryableCollectionLimits.MaxQueryLength + 1) },
            "filter-count" => new() { Filters = Enumerable.Range(0, QueryableCollectionLimits.MaxFilterCount + 1)
                .ToDictionary(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => "x") },
            "filter-empty-key" => new() { Filters = new Dictionary<string, string> { [""] = "x" } },
            "filter-key" => new() { Filters = new Dictionary<string, string> { [new string('x', 129)] = "x" } },
            "filter-value" => new() { Filters = new Dictionary<string, string> { ["value"] = new string('x', QueryableCollectionLimits.MaxFilterLength + 1) } },
            _ => new(),
        };
        if (fault is "filter-empty-key" or "filter-key")
            document.Columns = query.Filters.Keys.Select(key => new QueryableColumn(key, "Value")).ToArray();
        Assert.ThrowsAny<ArgumentException>(() => fixture.Session.QueryCollection(fault == "id" ? " " : "items",
            fault == "null" ? null! : query));
        Assert.Equal(0, document.Queries);
    }

    [Fact]
    public void QueryProtocolBoundsAreInclusive()
    {
        using var document = new QueryProbe();
        using var fixture = new MutationSessionFixture(document);
        var filters = Enumerable.Range(0, QueryableCollectionLimits.MaxFilterCount).ToDictionary(
            i => i == 0 ? new string('k', 128) : i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => new string('v', QueryableCollectionLimits.MaxFilterLength));
        document.Columns = filters.Keys.Select(key => new QueryableColumn(key, "Value")).ToArray();
        var query = new QueryableCollectionQuery { Limit = QueryableCollectionLimits.MaxPageSize,
            Cursor = new string('c', QueryableCollectionLimits.MaxCursorLength),
            Text = new string('t', QueryableCollectionLimits.MaxQueryLength), Filters = filters };
        Assert.Equal(2, fixture.Session.QueryCollection("items", query).Rows.Count);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("limit")]
    [InlineData("key")]
    [InlineData("mixed-empty")]
    public void InvalidProviderPagesAreRejectedIndependently(string fault)
    {
        using var document = new QueryProbe { EmptyRows = new("value") };
        document.Transform = page => fault switch
        {
            "id" => page with { CollectionId = "wrong" },
            "limit" => page with { Limit = page.Limit + 1 },
            "key" => page with { Rows = [page.Rows[0] with { Key = " " }, page.Rows[1]] },
            _ => page with { Rows = [page.Rows[0] with { IsEmpty = true }, page.Rows[1]] },
        };
        using var fixture = new MutationSessionFixture(document);
        Assert.Throws<InvalidOperationException>(() => fixture.Session.QueryCollection("items", new() { IncludeEmpty = false }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenEmptyCountsAreRejectedWhenAllRowsWereRequested(bool metadata)
    {
        using var document = new QueryProbe { EmptyRows = metadata ? new("value") : null,
            Transform = page => page with { FilteredCount = 1, HiddenEmptyCount = 1, Rows = [page.Rows[0]] } };
        using var fixture = new MutationSessionFixture(document);
        Assert.Throws<InvalidOperationException>(() => fixture.Session.QueryCollection("items", new() { IncludeEmpty = true }));
    }

    [Fact]
    public void EmptyCollectionsAndMixedNonemptyRowsAreValid()
    {
        using var empty = new QueryProbe { Total = 0 };
        using var fixture = new MutationSessionFixture(empty);
        Assert.Empty(fixture.Session.QueryCollection("items", new()).Rows);
        using var mixed = new QueryProbe { EmptyRows = new("value") };
        using var other = new MutationSessionFixture(mixed);
        Assert.Equal(2, other.Session.QueryCollection("items", new() { IncludeEmpty = false }).Rows.Count);
    }

    internal sealed class QueryProbe : BoundSaveDocument, IQueryableDocument
    {
        public IReadOnlyList<QueryableColumn> Columns { get; set; } = [new("value", "Value")];
        public long Total { get; init; } = 2;
        public int Queries { get; private set; }
        public EmptyRowPresentation? EmptyRows { get; init; }
        public Func<QueryableCollectionPage, QueryableCollectionPage>? Transform { get; set; }
        public override string Title => "Query probe";
        public IReadOnlyList<QueryableCollection> Collections => [new("items", Title, Columns, Total) { EmptyRows = EmptyRows }];
        public override ViewNode BuildView() => Collections[0].ToTableNode();
        public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
        {
            Queries++;
            var rows = Enumerable.Range(0, (int)Total).Skip(query.Offset).Take(query.Limit)
                .Select(i => new TableRow(i.ToString(System.Globalization.CultureInfo.InvariantCulture), new Dictionary<string, ViewNode>())).ToArray();
            var page = new QueryableCollectionPage(collectionId, Columns, rows, Total, Total, query.Offset, query.Limit);
            return Transform?.Invoke(page) ?? page;
        }
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
