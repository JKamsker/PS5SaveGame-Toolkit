using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class InMemoryCollectionTests
{
    private sealed record Item(string Key, string? Name, Int128? Number, string Category = "Item", bool Empty = false);
    private static readonly Item[] Items =
    [
        new("slot/z", "Potion", (Int128)ulong.MaxValue, Empty: true),
        new("slot/b", "POTION", (Int128)ulong.MaxValue - 1),
        new("slot/a", null, null, "Other"),
        new("slot/c", "Potion", (Int128)ulong.MaxValue - 1),
    ];
    private static readonly QueryableCollection Metadata = new("items", "Inventory",
        [new("name", "Name"), new("number", "Number"), new("category", "Category"), new("icon", "Icon")], Items.Length)
    { EmptyRows = new("name"), Description = "Saved slots" };

    private static InMemoryColumn<Item>[] Columns =>
    [
        new("name") { SearchText = item => item.Name, FilterText = item => item.Name,
            Comparer = Comparer<Item>.Create((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name)) },
        new("number") { FilterText = item => item.Number?.ToString(),
            Comparer = Comparer<Item>.Create((a, b) => Nullable.Compare(a.Number, b.Number)) },
        new("category") { SearchText = item => item.Category, FilterText = item => item.Category },
        new("icon"),
    ];

    private static InMemoryCollection<Item> Create(IEnumerable<Item>? items = null,
        InMemoryCollectionOptions<Item>? options = null, Action<Item>? rendered = null) =>
        new(Metadata, () => items ?? Items, item => item.Key, Columns, item =>
        {
            rendered?.Invoke(item);
            return new(item.Key, new Dictionary<string, ViewNode> { ["name"] = new TextNode(item.Name ?? "Unknown") });
        }, (options ?? new()) with { IsEmpty = item => item.Empty });

    [Fact]
    public void TypedSortPreservesUInt64Precision_NullsAndAscendingKeyTiesInBothDirections()
    {
        var collection = Create();
        var query = new QueryableCollectionQuery { SortColumn = "number", IncludeEmpty = true };
        Assert.Equal(["slot/a", "slot/b", "slot/c", "slot/z"], collection.Query(query).Rows.Select(row => row.Key));
        Assert.Equal(["slot/z", "slot/b", "slot/c", "slot/a"], collection.Query(query with { Descending = true }).Rows.Select(row => row.Key));
        var nullLast = Columns;
        nullLast[1] = nullLast[1] with { Comparer = Comparer<Item>.Create((a, b) =>
            a.Number is null ? b.Number is null ? 0 : 1 : b.Number is null ? -1 : a.Number.Value.CompareTo(b.Number.Value)) };
        var custom = new InMemoryCollection<Item>(Metadata, () => Items, item => item.Key, nullLast,
            item => new(item.Key, new Dictionary<string, ViewNode>()), new() { IsEmpty = item => item.Empty });
        Assert.Equal("slot/a", custom.Query(query).Rows[^1].Key);
    }

    [Fact]
    public void FiltersAndSearchRunBeforeEmptyHandling_CountingAndPageOnlyRendering()
    {
        var rendered = new List<string>();
        var collection = Create(rendered: item => rendered.Add(item.Key));
        var query = new QueryableCollectionQuery { Text = "pot", Filters = new Dictionary<string, string>
            { ["name"] = "POTION", ["category"] = "item" }, Limit = 1, Offset = 1 };
        var page = collection.Query(query);
        Assert.Equal((4L, 2L, 1L, 1, 1), (page.TotalCount, page.FilteredCount, page.HiddenEmptyCount, page.Offset, page.Limit));
        Assert.Equal(["slot/c"], rendered);
        Assert.Equal("slot/c", Assert.Single(page.Rows).Key);
        var shown = collection.Query(query with { IncludeEmpty = true, Offset = 2 });
        Assert.Equal((3L, 0L), (shown.FilteredCount, shown.HiddenEmptyCount));
        Assert.True(Assert.Single(shown.Rows).IsEmpty);
        Assert.Equal("slot/z", shown.Rows[0].Key);
        Assert.Equal("Potion", Assert.IsType<TextNode>(shown.Rows[0].Cells["name"]).Text);
        Assert.Equal(0, collection.Query(query with { Text = "Other" }).HiddenEmptyCount);
    }

    [Fact]
    public void DefaultOrderingAndWhitespacePoliciesAreExplicit()
    {
        var collection = Create();
        Assert.Equal(["slot/a", "slot/b", "slot/c"], collection.Query(new()).Rows.Select(row => row.Key));
        Assert.Equal(["slot/c", "slot/b", "slot/a"], collection.Query(new() { Descending = true }).Rows.Select(row => row.Key));
        Assert.Empty(collection.Query(new() { Text = " Potion " }).Rows);
        var options = new InMemoryCollectionOptions<Item>
        {
            NormalizeSearchText = text => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            DefaultComparer = Comparer<Item>.Create((a, b) => StringComparer.Ordinal.Compare(a.Category, b.Category)),
            ReverseDefaultOrderWhenDescending = false,
            ExtraSearchText = item => item.Key,
        };
        var custom = Create(options: options);
        Assert.Equal(["slot/b", "slot/c", "slot/a"], custom.Query(new() { Text = " " }).Rows.Select(row => row.Key));
        Assert.Equal(["slot/b", "slot/c"], custom.Query(new() { Text = " Potion " }).Rows.Select(row => row.Key));
        Assert.Equal("slot/a", Assert.Single(custom.Query(new() { Text = "slot/a" }).Rows).Key);
        Assert.Equal(custom.Query(new()).Rows.Select(row => row.Key), custom.Query(new() { Descending = true }).Rows.Select(row => row.Key));
    }

    [Fact]
    public void CustomMatchersCanRetainCaseSensitiveSearchAndSubstringFilters()
    {
        var custom = Create(options: new()
        {
            SearchMatches = (value, text) => value?.Contains(text, StringComparison.Ordinal) == true,
            FilterMatches = (value, text) => value?.StartsWith(text, StringComparison.Ordinal) == true,
        });
        var page = custom.Query(new() { Text = "POTION", Filters = new Dictionary<string, string> { ["name"] = "POT" } });
        Assert.Equal("slot/b", Assert.Single(page.Rows).Key);
    }

    [Fact]
    public void PagingClampsOrRejectsExplicitly_AndHandlesEmptyAndPastEndPages()
    {
        var collection = Create();
        var page = collection.Query(new() { Offset = -1, Limit = int.MaxValue });
        Assert.Equal((0, QueryableCollectionLimits.MaxPageSize), (page.Offset, page.Limit));
        Assert.Equal(1, collection.Query(new() { Limit = 0 }).Limit);
        var past = collection.Query(new() { Offset = int.MaxValue });
        Assert.Empty(past.Rows);
        Assert.Equal(3, past.FilteredCount);
        Assert.Empty(Create([]).Query(new()).Rows);
        var strict = Create(options: new() { ClampPaging = false });
        Assert.Throws<ArgumentException>(() => strict.Query(new() { Offset = -1 }));
        Assert.Throws<ArgumentException>(() => strict.Query(new() { Limit = 0 }));
        Assert.Throws<ArgumentException>(() => strict.Query(new() { Limit = 201 }));
    }

    [Theory]
    [InlineData("cursor")]
    [InlineData("unknown-sort")]
    [InlineData("unsortable")]
    [InlineData("unknown-filter")]
    [InlineData("unfilterable")]
    [InlineData("text")]
    [InlineData("filter-length")]
    [InlineData("filter-count")]
    public void UnsupportedQueriesFailBeforeRendering(string kind)
    {
        var query = kind switch
        {
            "cursor" => new QueryableCollectionQuery { Cursor = "" },
            "unknown-sort" => new() { SortColumn = "missing" },
            "unsortable" => new() { SortColumn = "icon" },
            "unknown-filter" => new() { Filters = new Dictionary<string, string> { ["missing"] = "x" } },
            "unfilterable" => new() { Filters = new Dictionary<string, string> { ["icon"] = "x" } },
            "text" => new() { Text = new string('x', 257) },
            "filter-length" => new() { Filters = new Dictionary<string, string> { ["name"] = new string('x', 257) } },
            _ => new() { Filters = Enumerable.Range(0, 33).ToDictionary(i => i.ToString(), _ => "x") },
        };
        var rendered = 0;
        Assert.Throws<ArgumentException>(() => Create(rendered: _ => rendered++).Query(query));
        Assert.Equal(0, rendered);
    }

    [Fact]
    public void InvalidModelKeysAndRendererKeysAreRejected_IncludingHiddenDuplicates()
    {
        Assert.Throws<InvalidOperationException>(() => Create([Items[0], Items[0]]).Query(new() { Text = "no match" }));
        Assert.Throws<InvalidOperationException>(() => Create([Items[0] with { Key = "" }]).Query(new()));
        var collection = new InMemoryCollection<Item>(Metadata, () => Items, item => item.Key, Columns,
            _ => new("page/0", new Dictionary<string, ViewNode>()), new() { IsEmpty = item => item.Empty });
        Assert.Throws<InvalidOperationException>(() => collection.Query(new()));
    }

    [Fact]
    public void SourceIsRefreshed_AndTableShellPreservesMetadata()
    {
        var items = new List<Item> { Items[1] };
        var collection = Create(items);
        Assert.Equal(1, collection.Query(new()).TotalCount);
        items.Add(Items[2]);
        Assert.Equal(2, collection.Query(new()).TotalCount);
        var shell = Metadata.ToTableNode();
        Assert.Equal((Metadata.Id, Metadata.Title, Metadata.Columns, Metadata.TotalCount, Metadata.Description, Metadata.EmptyRows),
            (shell.CollectionId, shell.Title, shell.Columns, shell.TotalCount, shell.Description, shell.EmptyRows));
        Assert.Throws<ArgumentException>(() => new InMemoryCollection<Item>(Metadata, () => Items, item => item.Key, Columns,
            item => new(item.Key, new Dictionary<string, ViewNode>())));
    }
}
