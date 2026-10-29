using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasQueryContractTests
{
    private const string Collection = "gvas/properties";
    private const int IntegerCount = 251;
    private const int TotalCount = IntegerCount + 1;

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 37)]
    [InlineData(false, 200)]
    [InlineData(true, 1)]
    [InlineData(true, 37)]
    [InlineData(true, 200)]
    public void EveryPageMatchesAuthoredOrdinalValuesAndCanonicalTieBreaks(bool descending, int pageSize)
    {
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("query.sav", Save()));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var entries = Enumerable.Range(0, IntegerCount).Select(index => (Key: Key(index), Value: Value(index))).ToArray();
        // Saved-value columns are ordinal display text, including signed numbers, not numeric sorts.
        var ordered = descending
            ? entries.OrderByDescending(entry => entry.Value, StringComparer.Ordinal).ThenBy(entry => entry.Key, StringComparer.Ordinal)
            : entries.OrderBy(entry => entry.Value, StringComparer.Ordinal).ThenBy(entry => entry.Key, StringComparer.Ordinal);
        var query = new QueryableCollectionQuery
        {
            SortColumn = "value", Descending = descending, Limit = pageSize,
            Filters = new Dictionary<string, string> { ["type"] = "iNtPrOpErTy" },
        };
        var rows = ReadAll(queryable, query, IntegerCount);
        Assert.Equal(ordered.Select(entry => (entry.Key, entry.Value)), rows);
        Assert.Empty(queryable.Query(Collection, query with { Offset = int.MaxValue }).Rows);
    }

    [Fact]
    public void CombinedFiltersUseTheWholeLiveModelAndDefaultOrderingIsIndependentOfValue()
    {
        byte[] bytes = Save();
        var plugin = new UnrealGvasPlugin();
        using var document = plugin.Open(MemorySaveSet.SingleFile("query.sav", bytes));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var query = new QueryableCollectionQuery
        {
            Text = "rEpEaTeD", Limit = 3,
            Filters = new Dictionary<string, string> { ["type"] = "INTPROPERTY", ["value"] = "-3" },
        };
        int[] matching = Enumerable.Range(0, IntegerCount).Where(index => index % 2 == 0 && Value(index) == "-3").ToArray();
        Assert.Equal(matching.Select(Key).Order(StringComparer.Ordinal), ReadAll(queryable, query, matching.Length).Select(row => row.Key));
        Assert.Empty(queryable.Query(Collection, query with { Text = "absent-name" }).Rows);
        Assert.Equal(0, queryable.Query(Collection, query with { Text = "absent-name" }).FilteredCount);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());

        _ = GvasFormatTests.Expand(document);
        int removed = matching[0];
        Assert.True(document.ApplyEdits([new(Key(0), FieldValue.Of(-3)), new(Key(removed), FieldValue.Of(99))]).Succeeded);
        string[] expected = matching.Where(index => index != removed).Append(0).Select(Key).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, ReadAll(queryable, query, expected.Length).Select(row => row.Key));
        byte[] changed = Assert.Single(document.Serialize()).Content.ToArray();
        using var reopened = plugin.Open(MemorySaveSet.SingleFile("query.sav", changed));
        Assert.Equal(expected, ReadAll(Assert.IsAssignableFrom<IQueryableDocument>(reopened), query, expected.Length).Select(row => row.Key));
        Assert.True(document.ApplyEdits([new(Key(0), FieldValue.Of(-8)), new(Key(removed), FieldValue.Of(-3))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static List<(string Key, string Value)> ReadAll(IQueryableDocument document, QueryableCollectionQuery query, int expectedCount)
    {
        var result = new List<(string, string)>();
        for (int offset = 0; offset < expectedCount + query.Limit; offset += query.Limit)
        {
            var page = document.Query(Collection, query with { Offset = offset });
            Assert.Equal((TotalCount, expectedCount, offset, query.Limit), (page.TotalCount, page.FilteredCount, page.Offset, page.Limit));
            Assert.Equal(Math.Min(query.Limit, Math.Max(0, expectedCount - offset)), page.Rows.Count);
            result.AddRange(page.Rows.Select(row => (row.Key, Assert.IsType<TextNode>(row.Cells["value"]).Text)));
        }
        return result;
    }

    private static string Key(int index) => $"property/{index}";
    private static string Value(int index) => (index % 17 - 8).ToString(CultureInfo.InvariantCulture);

    private static byte[] Save() => GvasFixture.Build(writer =>
    {
        for (int index = 0; index < IntegerCount; index++)
            GvasFixture.Property(writer, index % 2 == 0 ? "Repeated" : "Other", "IntProperty",
                GvasFixture.Body(body => body.Write(index % 17 - 8)));
        GvasFixture.Property(writer, "Repeated", "StrProperty", GvasFixture.Body(body => GvasFixture.String(body, "-3")));
    });
}
