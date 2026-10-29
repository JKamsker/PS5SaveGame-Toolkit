using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasCollectionTests
{
    [Fact]
    public void AllNestedRowsAreQueryableWithoutExpandingAndDuplicateNamesKeepCanonicalIdentity()
    {
        var bytes = GvasFixture.Nested();
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("nested.sav", bytes));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var first = queryable.Query("gvas/properties", new() { Text = "Caption", Limit = 1, SortColumn = "path" });
        var second = queryable.Query("gvas/properties", new() { Text = "Caption", Limit = 1, Offset = 1, SortColumn = "path" });
        Assert.Equal(2, first.FilteredCount);
        Assert.NotEqual(first.Rows[0].Key, second.Rows[0].Key);
        var fields = GvasFormatTests.Expand(document).ToDictionary(field => field.ValueId);
        Assert.Equal("Item0", document.GetValue(first.Rows[0].Key).AsString());
        Assert.Equal("Item1", document.GetValue(second.Rows[0].Key).AsString());
        Assert.True(fields.ContainsKey(first.Rows[0].Key));
        Assert.All(first.Rows.SelectMany(row => row.Cells.Where(pair => pair.Key != "value").Select(pair => pair.Value)),
            cell => Assert.IsType<TextNode>(cell));
        Assert.All(first.Rows, row => Assert.IsType<TextNode>(row.Cells["value"]));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void CanonicalScalarEditsUpdateEveryProjectionAndUndoRestoresExactBytes()
    {
        var bytes = GvasFixture.Nested();
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("nested.sav", bytes));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var row = queryable.Query("gvas/properties", new() { Text = "Item0" }).Rows.Single();
        var field = GvasFormatTests.Expand(document).Single(field => field.ValueId == row.Key);
        Assert.True(document.ApplyEdits([new(row.Key, FieldValue.Of("Longer renamed item 雪"))]).Succeeded);
        var changed = queryable.Query("gvas/properties", new() { Text = "Longer renamed" });
        Assert.Equal(row.Key, Assert.Single(changed.Rows).Key);
        var serialized = Assert.Single(document.Serialize()).Content.ToArray();
        using var reopened = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("nested.sav", serialized));
        var after = Assert.IsAssignableFrom<IQueryableDocument>(reopened).Query("gvas/properties", new() { Text = "Longer renamed" });
        Assert.Equal(row.Key, Assert.Single(after.Rows).Key);
        Assert.True(document.ApplyEdits([new(row.Key, field.Value)]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void PagingFiltersSortAndBoundsAreDeterministic()
    {
        var bytes = GvasFixture.RepeatedIntegers(251);
        var file = MemorySaveSet.SingleFile("many.sav", bytes);
        var plugin = new UnrealGvasPlugin();
        using var document = plugin.Open(file);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.Single(queryable.Collections);
        var query = new QueryableCollectionQuery { Limit = 200, SortColumn = "value", Descending = true, Filters = new Dictionary<string, string> { ["type"] = "IntProperty" } };
        var first = queryable.Query("gvas/properties", query);
        var second = queryable.Query("gvas/properties", query with { Offset = 200 });
        Assert.Equal(251, first.TotalCount);
        Assert.Equal(251, first.FilteredCount);
        Assert.Equal(200, first.Rows.Count);
        Assert.Equal(51, second.Rows.Count);
        Assert.Empty(first.Rows.Select(row => row.Key).Intersect(second.Rows.Select(row => row.Key)));
        Assert.Equal(first.Rows.Select(row => row.Key), queryable.Query("gvas/properties", query).Rows.Select(row => row.Key));
        var raw = Assert.IsAssignableFrom<IQueryableDocument>(plugin.Open(file));
        using ((ISaveDocument)raw)
        {
            Assert.Throws<ArgumentException>(() => raw.Query("gvas/properties", new() { Limit = 201 }));
            Assert.Throws<ArgumentException>(() => raw.Query("gvas/properties", new() { Offset = -1 }));
            Assert.Throws<ArgumentException>(() => raw.Query("gvas/properties", new() { Cursor = "bad" }));
            Assert.Throws<ArgumentException>(() => raw.Query("gvas/properties", new() { Text = new string('x', 257) }));
            Assert.Throws<ArgumentException>(() => raw.Query("gvas/properties", new() { SortColumn = "missing" }));
            Assert.Throws<ArgumentException>(() => raw.Query("gvas/properties", new() { Filters = new Dictionary<string, string> { ["missing"] = "x" } }));
            Assert.Throws<KeyNotFoundException>(() => raw.Query("unknown", new()));
        }
    }

    [Fact]
    public void WideEscapedCellsStayBelowHostResponseBudgetAndUntruncatedTextRemainsSearchable()
    {
        var large = new string('雪', 4096) + "unique-tail";
        var bytes = GvasFixture.Build(writer =>
        {
            for (var i = 0; i < 200; i++) GvasFixture.Property(writer, large, "StrProperty", GvasFixture.Body(body => GvasFixture.String(body, large)));
        });
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("wide.sav", bytes));
        var page = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("gvas/properties", new() { Limit = 200, Text = "unique-tail" });
        Assert.Equal(200, page.Rows.Count);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(page).Length < 2 * 1024 * 1024);
        Assert.All(page.Rows, row => Assert.True(((TextNode)row.Cells["value"]).Text.Length < 256));
    }

    [Fact]
    public void GenericTypeCatalogIsAvailableWithoutATitleAndAttributionAppearsOnEachRow()
    {
        var plugin = new UnrealGvasPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var references = store.Pin(plugin, "UNKNOWN");
        var catalog = references.Catalog("unrealgvas_property_types");
        Assert.NotNull(catalog);
        Assert.Equal(17, catalog.Count);
        Assert.Contains("1917a22", catalog.Find("IntProperty")!.AttributionText);
        using var document = plugin.Open(MemorySaveSet.SingleFile("data.sav", GvasFixture.Primitives()), references);
        var page = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("gvas/properties", new() { Filters = new Dictionary<string, string> { ["name"] = "Count" } });
        Assert.Contains("trumank", ((TextNode)Assert.Single(page.Rows).Cells["source"]).Text);
    }
}
