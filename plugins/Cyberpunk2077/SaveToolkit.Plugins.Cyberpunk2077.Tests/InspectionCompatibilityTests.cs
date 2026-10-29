using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class InspectionCompatibilityTests
{
    [Fact]
    public void TextNumericTiesWhitespaceAndEmptyValuesKeepTheirOrdering()
    {
        InspectionRow[] rows = [Row("c", "Alpha", "10"), Row("a", "alpha", "10"),
            Row("b", " alpha", "2"), Row("d", "", ""), Row("e", "Beta", "18446744073709551615")];
        var collection = new InspectionCollection<InspectionRow>(new("compatibility", "Compatibility",
            [new("name", "Name"), new("value", "Value", new NullableSpec(IntegerSpec.UInt64))], rows.Length),
            rows, row => row.Key, new Dictionary<string, Func<InspectionRow, string>>
            { ["name"] = row => row.Values["name"], ["value"] = row => row.Values["value"] },
            row => new(row.Key, new Dictionary<string, ViewNode>()), extraSearch: row => row.Key == "e" ? "extra identifier" : "");
        string[] Keys(QueryableCollectionQuery query) => collection.Query(query).Rows.Select(row => row.Key).ToArray();

        Assert.Equal(["a", "b", "c", "d", "e"], Keys(new()));
        Assert.Equal(["e", "d", "c", "b", "a"], Keys(new() { Descending = true }));
        Assert.Equal(["d", "b", "a", "c", "e"], Keys(new() { SortColumn = "name" }));
        Assert.Equal(["e", "a", "c", "b", "d"], Keys(new() { SortColumn = "name", Descending = true }));
        Assert.Equal(["d", "b", "a", "c", "e"], Keys(new() { SortColumn = "value" }));
        Assert.Equal(["e", "a", "c", "b", "d"], Keys(new() { SortColumn = "value", Descending = true }));
        Assert.Equal(Keys(new()), Keys(new() { Text = "" }));
        Assert.Equal(["b", "e"], Keys(new() { Text = " " }));
        Assert.Equal(["e"], Keys(new() { Text = "EXTRA IDENTIFIER" }));
        Assert.Equal(["a", "c"], Keys(new() { Filters = new Dictionary<string, string> { ["name"] = "ALPHA" } }));
        Assert.Equal(["d"], Keys(new() { Filters = new Dictionary<string, string> { ["value"] = "" } }));
        Assert.Equal(["a"], Keys(new() { Offset = -1, Limit = 0 }));
    }

    [Fact]
    public void InvalidQueriesKeepTheirOriginalMessagesAndValidationOrder()
    {
        var collection = new InspectionCollection<int>(new("validation", "Validation", [new("value", "Value")], 1),
            [0], _ => "row", new Dictionary<string, Func<int, string>> { ["value"] = _ => "" },
            _ => new("row", new Dictionary<string, ViewNode>()));
        void Reject(QueryableCollectionQuery query, string message) =>
            Assert.Equal(new ArgumentException(message, nameof(query)).Message,
                Assert.Throws<ArgumentException>(() => collection.Query(query)).Message);

        Assert.Throws<ArgumentNullException>(() => collection.Query(null!));
        Reject(new() { Cursor = "cursor", Text = new('x', QueryableCollectionLimits.MaxQueryLength + 1) }, "This collection uses offset paging.");
        Reject(new() { Text = new('x', QueryableCollectionLimits.MaxQueryLength + 1) }, "Collection query exceeds the supported bounds.");
        Reject(new() { Filters = new Dictionary<string, string> { ["missing"] = "x" }, SortColumn = "missing" }, "Unknown filter column 'missing'.");
        Reject(new() { SortColumn = "missing" }, "Unknown sort column 'missing'.");
        Reject(new() { Filters = new Dictionary<string, string> { ["missing"] = new('x', QueryableCollectionLimits.MaxFilterLength + 1) } },
            "Collection query exceeds the supported bounds.");
    }

    private static InspectionRow Row(string key, string name, string value) =>
        new(key, new Dictionary<string, string> { ["name"] = name, ["value"] = value });
}


