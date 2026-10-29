using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class EmptySlotQueryTests
{
    [Fact]
    public void VisibilityFiltersTheCompleteModelBeforeSortingAndPaging()
    {
        var built = 0;
        var rows = Enumerable.Range(0, 1000).Select(index => new InspectionRow($"slot/{index:D4}",
            new Dictionary<string, string>
            {
                ["name"] = index % 2 == 0 ? "Empty slot" : "Unknown item",
                ["slot"] = index.ToString(CultureInfo.InvariantCulture),
                ["area"] = index < 500 ? "Weapons" : "Cyberware",
            }, IsEmpty: index % 2 == 0)).ToArray();
        var table = new InspectionTable("slots", "Stored slots", [new("name", "Item"),
            new("slot", "Slot", IntegerSpec.UInt32), new("area", "Area")], rows, "Synthetic stored slots",
            cell: (_, _) => { built++; return null; }, emptyRows: new("name"));
        Assert.Equal(table.Metadata.EmptyRows, table.View().EmptyRows);
        Assert.True(table.View().EmptyRows!.HideByDefault);
        Assert.Equal(0, built);

        var query = new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["area"] = "cyberware" },
            SortColumn = "slot", Descending = true, Limit = 2 };
        var hidden = table.Query(query);
        Assert.Equal((1000, 250, 250), (hidden.TotalCount, hidden.FilteredCount, hidden.HiddenEmptyCount));
        Assert.Equal(["slot/0999", "slot/0997"], hidden.Rows.Select(row => row.Key));
        Assert.All(hidden.Rows, row => Assert.False(row.IsEmpty));
        Assert.Equal(6, built); // Only two rows' three cells were constructed.
        var next = table.Query(query with { Offset = 2 });
        Assert.Equal(["slot/0995", "slot/0993"], next.Rows.Select(row => row.Key));
        Assert.Equal(hidden.HiddenEmptyCount, next.HiddenEmptyCount);

        var shown = table.Query(query with { IncludeEmpty = true });
        Assert.Equal((1000, 500, 0), (shown.TotalCount, shown.FilteredCount, shown.HiddenEmptyCount));
        Assert.Equal(["slot/0999", "slot/0998"], shown.Rows.Select(row => row.Key));
        Assert.True(shown.Rows[1].IsEmpty);
        Assert.Equal("Empty slot", ReadOnlyInspectionSampleTests.Cell(shown.Rows[1], "name"));
        var emptyOnly = table.Query(query with { Text = "Empty slot" });
        Assert.Empty(emptyOnly.Rows);
        Assert.Equal((0, 250), (emptyOnly.FilteredCount, emptyOnly.HiddenEmptyCount));
        Assert.Equal(0, table.Query(query with { Text = "Unknown item" }).HiddenEmptyCount);
        Assert.Equal(0, table.Query(query with { Text = "not present" }).HiddenEmptyCount);
        var pastEnd = table.Query(query with { Offset = int.MaxValue });
        Assert.Empty(pastEnd.Rows);
        Assert.Equal(hidden.HiddenEmptyCount, pastEnd.HiddenEmptyCount);
    }

    [Fact]
    public void ExplicitHideOverridesACollectionThatShowsEmptyRowsByDefault()
    {
        InspectionRow[] rows = [new("stored/0", new Dictionary<string, string> { ["item"] = "Empty slot" }, IsEmpty: true)];
        var table = new InspectionTable("stored", "Stored", [new("item", "Item")], rows, "Stored",
            emptyRows: new("item") { HideByDefault = false });
        Assert.True(Assert.Single(table.Query(new()).Rows).IsEmpty);
        Assert.Equal(0, table.Query(new()).HiddenEmptyCount);
        var hidden = table.Query(new() { IncludeEmpty = false });
        Assert.Empty(hidden.Rows);
        Assert.Equal((1, 0, 1), (hidden.TotalCount, hidden.FilteredCount, hidden.HiddenEmptyCount));
    }

    [Fact]
    public void CollectionsWithoutEmptyPresentationKeepTheirExistingVisibility()
    {
        InspectionRow[] rows = [new("stored/0", new Dictionary<string, string> { ["item"] = "Empty slot" }, IsEmpty: true)];
        var table = new InspectionTable("stored", "Stored", [new("item", "Item")], rows, "Stored");
        var page = table.Query(new() { IncludeEmpty = false });
        Assert.Null(table.View().EmptyRows);
        Assert.True(Assert.Single(page.Rows).IsEmpty);
        Assert.Equal((1, 1, 0), (page.TotalCount, page.FilteredCount, page.HiddenEmptyCount));
    }
}
