using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class InMemoryCollectionMutationEffectivenessTests
{
    private static readonly QueryableCollection Metadata = new("items", "Items", [new("value", "Value")], 1);
    private static readonly InMemoryColumn<string>[] Columns = [new("value") { SearchText = value => value, FilterText = value => value }];

    [Theory]
    [InlineData("metadata")]
    [InlineData("source")]
    [InlineData("key")]
    [InlineData("columns")]
    [InlineData("renderRow")]
    public void ConstructorRejectsMissingAdapters(string argument) =>
        Assert.Throws<ArgumentNullException>(argument, () => new InMemoryCollection<string>(
            argument == "metadata" ? null! : Metadata,
            argument == "source" ? null! : () => ["item"],
            argument == "key" ? null! : value => value,
            argument == "columns" ? null! : Columns,
            argument == "renderRow" ? null! : value => new(value, new Dictionary<string, ViewNode>())));

    [Fact]
    public void NullQueriesAndNullTableMetadataFailAtTheBoundary()
    {
        Assert.Throws<ArgumentNullException>("query", () => Create().Query(null!));
        Assert.Throws<ArgumentNullException>("metadata", () => QueryableCollectionExtensions.ToTableNode(null!));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MetadataColumnsMustBeUniqueAndMatchTheAdapters(bool duplicate)
    {
        var metadata = Metadata with { Columns = duplicate ? [new("value", "One"), new("value", "Two")] : [new("other", "Other")] };
        Assert.Throws<ArgumentException>(() => new InMemoryCollection<string>(metadata,
            () => ["item"], value => value, Columns, value => new(value, new Dictionary<string, ViewNode>())));
    }

    [Theory]
    [InlineData(QueryableCollectionLimits.MaxQueryLength, true)]
    [InlineData(QueryableCollectionLimits.MaxQueryLength + 1, false)]
    public void NormalizedSearchTextHasAnInclusiveBoundBeforeReadingTheSource(int length, bool accepted)
    {
        var reads = 0;
        var collection = new InMemoryCollection<string>(Metadata, () => { reads++; return ["item"]; },
            value => value, Columns, value => new(value, new Dictionary<string, ViewNode>()),
            new() { NormalizeSearchText = _ => new string('x', length), SearchMatches = (_, _) => true });
        if (accepted) Assert.Single(collection.Query(new() { Text = "x" }).Rows);
        else Assert.Throws<ArgumentException>(() => collection.Query(new() { Text = "x" }));
        Assert.Equal(accepted ? 1 : 0, reads);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(QueryableCollectionLimits.MaxPageSize)]
    public void StrictPagingAcceptsZeroOffsetAndBothLimitEndpoints(int limit)
    {
        var page = Create(new() { ClampPaging = false }).Query(new() { Offset = 0, Limit = limit });
        Assert.Single(page.Rows);
        Assert.Equal(limit, page.Limit);
    }

    [Fact]
    public void RawSearchAndFilterLengthsAcceptTheProtocolEndpoints()
    {
        var columnKey = new string('k', QueryableCollectionLimits.MaxFilterLength);
        var columns = Enumerable.Range(0, QueryableCollectionLimits.MaxFilterCount)
            .Select(i => new QueryableColumn(i == 0 ? columnKey : i.ToString(System.Globalization.CultureInfo.InvariantCulture), "Value")).ToArray();
        var adapters = columns.Select(column => new InMemoryColumn<string>(column.Key)
            { SearchText = value => value, FilterText = value => value }).ToArray();
        var collection = new InMemoryCollection<string>(Metadata with { Columns = columns }, () => ["item"],
            value => value, adapters, value => new(value, new Dictionary<string, ViewNode>()),
            new() { SearchMatches = (_, _) => true, FilterMatches = (_, _) => true });
        var page = collection.Query(new()
        {
            Text = new string('x', QueryableCollectionLimits.MaxQueryLength),
            Filters = columns.ToDictionary(column => column.Key, _ => new string('x', QueryableCollectionLimits.MaxFilterLength)),
        });
        Assert.Single(page.Rows);
    }

    private static InMemoryCollection<string> Create(InMemoryCollectionOptions<string>? options = null) =>
        new(Metadata, () => ["item"], value => value, Columns,
            value => new(value, new Dictionary<string, ViewNode>()), options);
}
