using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class InMemoryCollectionBehaviorTests
{
    private static readonly IReadOnlyDictionary<string, ViewNode> EmptyCells = new Dictionary<string, ViewNode>();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("key/0")]
    public void CompleteKeyValidationPrecedesPredicatesAndRendering(string? invalidKey)
    {
        var keys = Enumerable.Range(0, 100).Select(i => $"key/{i}").ToArray();
        keys[^1] = invalidKey!;
        var predicates = 0;
        var renders = 0;
        var collection = new InMemoryCollection<int>(
            new("items", "Items", [new("name", "Name")], keys.Length),
            () => Enumerable.Range(0, keys.Length), i => keys[i],
            [new("name") { SearchText = _ => { predicates++; return null; } }],
            i => { renders++; return new(keys[i], EmptyCells); },
            new() { IsEmpty = _ => { predicates++; return true; } });

        Assert.Throws<InvalidOperationException>(() => collection.Query(new()
            { Text = "no matches", IncludeEmpty = false, Limit = 1, Offset = int.MaxValue }));
        Assert.Equal(0, predicates);
        Assert.Equal(0, renders);
    }

    [Fact]
    public void StreamingSourceIsEnumeratedOnce_AndKeysAreCapturedOncePerModelRow()
    {
        var enumerations = 0;
        var keyCalls = 0;
        var renders = new List<int>();
        var keys = new[] { "z", "a", "b", "c" };
        IEnumerable<int> Source()
        {
            enumerations++;
            for (var i = 0; i < keys.Length; i++) yield return i;
        }
        var collection = new InMemoryCollection<int>(new("items", "Items", [], 0), Source,
            i => { keyCalls++; return keys[i]; }, [],
            i => { renders.Add(i); return new(keys[i], EmptyCells); });

        var page = collection.Query(new() { Offset = 1, Limit = 2 });

        Assert.Equal(1, enumerations);
        Assert.Equal(keys.Length, keyCalls);
        Assert.Equal(4, page.TotalCount); // Metadata is deliberately stale; count the complete source.
        Assert.Equal(["b", "c"], page.Rows.Select(row => row.Key));
        Assert.Equal([2, 3], renders);
        Assert.Empty(collection.Query(new() { Offset = int.MaxValue }).Rows);
        Assert.Equal(2, enumerations);
        Assert.Equal(keys.Length * 2, keyCalls);
        Assert.Equal([2, 3], renders);
    }

    [Fact]
    public void RepeatedQueryReadsLiveMembershipSortValuesEmptyFlagsKeysAndFilterCriteria()
    {
        var items = new List<MutableItem>
        {
            new("z", "match", 1), new("b", "match", 2), new("a", "match", 2), new("null", null, null),
        };
        var metadata = new QueryableCollection("items", "Items", [new("name", "Name")], items.Count)
            { EmptyRows = new("name") };
        var collection = new InMemoryCollection<MutableItem>(metadata, () => items, item => item.Key,
            [new("name") { SearchText = item => item.Name, FilterText = item => item.Name,
                Comparer = Comparer<MutableItem>.Create((a, b) => Nullable.Compare(a.Number, b.Number)) }],
            item => new(item.Key, new Dictionary<string, ViewNode> { ["name"] = new TextNode(item.Name ?? "null") }),
            new() { IsEmpty = item => item.Empty });
        var filters = new Dictionary<string, string> { ["name"] = "MATCH" };
        var query = new QueryableCollectionQuery { Text = "match", Filters = filters, SortColumn = "name", Descending = true };

        var original = collection.Query(query);
        Assert.Equal(["a", "b", "z"], original.Rows.Select(row => row.Key));
        items[0].Number = 3;
        Assert.Equal(["z", "a", "b"], collection.Query(query).Rows.Select(row => row.Key));
        items[0].Empty = true;
        var hidden = collection.Query(query);
        Assert.Equal(["a", "b"], hidden.Rows.Select(row => row.Key));
        Assert.Equal((4L, 2L, 1L), (hidden.TotalCount, hidden.FilteredCount, hidden.HiddenEmptyCount));
        var shown = collection.Query(query with { IncludeEmpty = true });
        Assert.True(shown.Rows[0].IsEmpty);
        Assert.Equal(0, shown.HiddenEmptyCount);

        items[1].Name = "other";
        items[2].Key = "A";
        items.Add(new("new", "match", 4));
        Assert.Equal(["new", "A"], collection.Query(query).Rows.Select(row => row.Key));
        Assert.Equal("b", Assert.Single(collection.Query(query with { Text = "other", Filters =
            new Dictionary<string, string> { ["name"] = "OTHER" } }).Rows).Key);
        filters["name"] = "other";
        Assert.Empty(collection.Query(query).Rows);
        Assert.Equal("b", Assert.Single(collection.Query(query with { Text = null }).Rows).Key);

        // A later query/compaction must not mutate a previously returned page or its cells.
        Assert.Equal(["a", "b", "z"], original.Rows.Select(row => row.Key));
        Assert.All(original.Rows, row => Assert.False(row.IsEmpty));
        Assert.Equal("match", Assert.IsType<TextNode>(original.Rows[1].Cells["name"]).Text);
        items[^1].Key = "A";
        Assert.Throws<InvalidOperationException>(() => collection.Query(query));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DescendingComparersKeepOrdinalAscendingTies_WithoutNegatingExtremeResults(bool defaultOrder)
    {
        var keys = new[] { "z", "i", "I", "a", "Z" };
        var values = new[] { 0, 1, 1, 2, 0 };
        var comparer = Comparer<int>.Create((a, b) => values[a] < values[b] ? int.MinValue
            : values[a] > values[b] ? int.MaxValue : 0);
        var collection = new InMemoryCollection<int>(new("items", "Items", [new("value", "Value")], keys.Length),
            () => Enumerable.Range(0, keys.Length), i => keys[i], [new("value") { Comparer = comparer }],
            i => new(keys[i], EmptyCells), new() { DefaultComparer = defaultOrder ? comparer : null });
        var query = new QueryableCollectionQuery { SortColumn = defaultOrder ? null : "value", Limit = 2 };
        var ascending = new List<string>();
        var descending = new List<string>();
        for (var offset = 0; offset < keys.Length; offset += query.Limit)
        {
            ascending.AddRange(collection.Query(query with { Offset = offset }).Rows.Select(row => row.Key));
            descending.AddRange(collection.Query(query with { Offset = offset, Descending = true }).Rows.Select(row => row.Key));
        }
        Assert.Equal(["Z", "z", "I", "i", "a"], ascending);
        Assert.Equal(["a", "I", "i", "Z", "z"], descending);
        Assert.Equal(["z", "i", "I", "a", "Z"], keys);
    }

    [Fact]
    public void RendererEmptyFlagIsPreservedWithoutPredicate_AndOverriddenByModelPredicate()
    {
        var keys = new[] { "a", "b" };
        var items = new[] { 0, 1 };
        InMemoryCollection<int> Create(InMemoryCollectionOptions<int>? options) => new(
            new("items", "Items", [], 2), () => items, i => keys[i], [],
            i => new(keys[i], EmptyCells) { IsEmpty = i == 0 }, options);

        var legacy = Create(null).Query(new());
        Assert.True(legacy.Rows[0].IsEmpty);
        Assert.False(legacy.Rows[1].IsEmpty);
        var model = Create(new() { IsEmpty = i => i == 1 }).Query(new());
        Assert.False(model.Rows[0].IsEmpty);
        Assert.True(model.Rows[1].IsEmpty);
    }

    private sealed class MutableItem(string key, string? name, int? number)
    {
        public string Key { get; set; } = key;
        public string? Name { get; set; } = name;
        public int? Number { get; set; } = number;
        public bool Empty { get; set; }
    }
}
