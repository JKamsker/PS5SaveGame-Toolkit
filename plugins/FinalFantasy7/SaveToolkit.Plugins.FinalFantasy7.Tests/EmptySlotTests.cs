using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class EmptySlotTests
{
    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void SlotTablesPublishOneCollectionAndPreserveAllSampleSlots(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(file =>
            schema.TryGetRole(file.FileName, out var common) && !common))
        {
            using var document = new FinalFantasy7Plugin().Open(file);
            var source = file.ReadAllBytes();
            var queryable = (IQueryableDocument)document;
            var view = Assert.IsType<TabsNode>(document.BuildView());
            foreach (var tab in new[] { "Inventory", "Materia" })
                Assert.Single(view.Tabs.Single(candidate => candidate.Title == tab).Children.OfType<QueryableTableNode>());
            Assert.DoesNotContain(view.Tabs.SelectMany(tab => tab.Children).OfType<SectionNode>(),
                section => section.Title.StartsWith("Slots ", StringComparison.Ordinal));

            foreach (var collection in queryable.Collections.Where(collection => collection.EmptyRows is not null))
            {
                var table = Assert.Single(view.Tabs.SelectMany(tab => tab.Children).OfType<QueryableTableNode>(),
                    table => table.CollectionId == collection.Id);
                Assert.Equal(new EmptyRowPresentation("name"), collection.EmptyRows);
                Assert.Equal(collection.EmptyRows, table.EmptyRows);
                var (offset, stride, count, marker) = Layout(schema, collection.Id);
                Assert.Equal(count, collection.TotalCount);
                var expectedEmpty = Enumerable.Range(0, count).Count(slot =>
                    BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset + slot * stride)) == marker);
                Assert.True(expectedEmpty > 0);
                var visible = ReadAll(queryable, collection.Id, includeEmpty: null);
                var shown = ReadAll(queryable, collection.Id, includeEmpty: true);
                Assert.Equal(count, shown.Count);
                Assert.Equal(expectedEmpty, shown.Count(row => row.IsEmpty));
                Assert.All(shown, row =>
                {
                    var slot = int.Parse(Assert.IsType<TextNode>(row.Cells["slot"]).Text, CultureInfo.InvariantCulture);
                    var type = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset + slot * stride));
                    Assert.Equal(type == marker, row.IsEmpty);
                    if (row.Cells.TryGetValue("id", out var id))
                        Assert.Equal(type.ToString(CultureInfo.InvariantCulture), Assert.IsType<FieldNode>(id).Value.AsString());
                });
                Assert.Equal(shown.Where(row => !row.IsEmpty).Select(row => row.Key), visible.Select(row => row.Key));
                Assert.All(shown.Where(row => row.IsEmpty), row =>
                    Assert.Equal("Empty slot", Assert.IsType<TextNode>(row.Cells["name"]).Text));
                var hidden = queryable.Query(collection.Id, new());
                Assert.Equal(expectedEmpty, hidden.HiddenEmptyCount);
                Assert.Equal(count - expectedEmpty, hidden.FilteredCount);
                Assert.Equal(0, queryable.Query(collection.Id, new() { IncludeEmpty = true }).HiddenEmptyCount);
            }
            Assert.Equal(source, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void QueriesCountMatchingEmptiesBeforeVisibilitySortAndPaging()
    {
        var entries = Enumerable.Range(0, 6).Select(slot => new CollectionEntry($"slot/{slot}",
            new Dictionary<string, string> { ["name"] = slot % 2 == 0 ? "Empty slot" : "Potion", ["slot"] = slot.ToString(CultureInfo.InvariantCulture) })
            { IsEmpty = slot % 2 == 0 }).ToArray();
        var collection = new CollectionQuery(new("slots", "Slots", [new("name", "Name"), new("slot", "Slot", IntegerSpec.UInt32)], entries.Length)
            { EmptyRows = EmptySlots.Presentation }, () => entries);
        var page = collection.Query(new() { SortColumn = "slot", Descending = true, Offset = 1, Limit = 1 });
        Assert.Equal("slot/3", Assert.Single(page.Rows).Key);
        Assert.Equal(6, page.TotalCount);
        Assert.Equal(3, page.FilteredCount);
        Assert.Equal(3, page.HiddenEmptyCount);
        var explicitHidden = collection.Query(new() { IncludeEmpty = false, SortColumn = "slot", Descending = true, Offset = 1, Limit = 1 });
        Assert.Equal(page.Rows.Select(row => row.Key), explicitHidden.Rows.Select(row => row.Key));
        Assert.Equal(page.HiddenEmptyCount, explicitHidden.HiddenEmptyCount);
        var search = collection.Query(new() { Text = "Empty slot" });
        Assert.Empty(search.Rows);
        Assert.Equal(0, search.FilteredCount);
        Assert.Equal(3, search.HiddenEmptyCount);
        var shown = collection.Query(new() { Text = "Empty slot", IncludeEmpty = true, Limit = 1 });
        Assert.True(Assert.Single(shown.Rows).IsEmpty);
        Assert.Equal(3, shown.FilteredCount);
        Assert.Equal(0, shown.HiddenEmptyCount);
        var exact = collection.Query(new() { Filters = new Dictionary<string, string> { ["slot"] = "2" } });
        Assert.Empty(exact.Rows);
        Assert.Equal(1, exact.HiddenEmptyCount);
        var occupied = collection.Query(new() { Text = "Potion" });
        Assert.Equal(3, occupied.FilteredCount);
        Assert.Equal(0, occupied.HiddenEmptyCount);
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void UnknownIdsAndZeroQuantityRemainVisibleAndEditsKeepNativeOffsets(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var file = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == schema.Prefix + "000");
        var original = file.ReadAllBytes();
        var (offset, stride, _, _) = Layout(schema, "inventory");
        const int unknownSlot = 2047;
        const int zeroSlot = 2046;
        const uint unknownId = 0xABCDEF12;
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(offset + unknownSlot * stride), unknownId);
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(offset + zeroSlot * stride), schema.IsRemake ? 0U : 100U);
        BinaryPrimitives.WriteInt32LittleEndian(original.AsSpan(offset + zeroSlot * stride + sizeof(uint)), 0);
        var plugin = new FinalFantasy7Plugin();
        using var document = plugin.Open(MemorySaveSet.SingleFile(file.FileName, original, title));
        var queryable = (IQueryableDocument)document;
        var unknown = Assert.Single(queryable.Query("inventory", new() { Text = $"0x{unknownId:X}" }).Rows);
        Assert.False(unknown.IsEmpty);
        Assert.StartsWith("Unknown ID", Assert.IsType<TextNode>(unknown.Cells["name"]).Text, StringComparison.Ordinal);
        var zero = Assert.Single(queryable.Query("inventory", new() { Filters = new Dictionary<string, string> { ["slot"] = zeroSlot.ToString(CultureInfo.InvariantCulture) } }).Rows);
        Assert.False(zero.IsEmpty);
        Assert.Equal(0, Assert.IsType<FieldNode>(zero.Cells["quantity"]).Value.AsInt32());
        var editable = ReadAll(queryable, "inventory", null).Select(row => Assert.IsType<FieldNode>(row.Cells["quantity"]))
            .First(field => field.Spec is IntegerSpec { ReadOnly: false } spec && field.Value.AsInt32() > 0 && field.Value.AsInteger() < spec.Max);
        var slot = int.Parse(editable.ValueId.Split('/')[1], CultureInfo.InvariantCulture);
        var next = editable.Value.AsInt32() + 1;
        var outcome = document.ApplyEdits([new(editable.ValueId, FieldValue.Of(next))]);
        Assert.True(outcome.Succeeded, string.Join("; ", outcome.Errors.Select(error => error.Message)));
        var expected = original.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(offset + slot * stride + sizeof(uint)), next);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.Equal(expected, edited);
        using var reopened = plugin.Open(MemorySaveSet.SingleFile(file.FileName, edited, title));
        var row = Assert.Single(((IQueryableDocument)reopened).Query("inventory", new() { Filters = new Dictionary<string, string> { ["slot"] = slot.ToString(CultureInfo.InvariantCulture) } }).Rows);
        Assert.Equal($"inventory/{slot}", row.Key);
        Assert.Equal(next, Assert.IsType<FieldNode>(row.Cells["quantity"]).Value.AsInt32());
        Assert.True(document.ApplyEdits([new(editable.ValueId, editable.Value)]).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void SelectAllMatchingExcludesHiddenEmptySlotsAndRetainsExclusions(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var file = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == schema.Prefix + "000");
        var plugin = new FinalFantasy7Plugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("empty-slots", file, plugin, document);
        _ = session.BuildView();
        var queryable = (IQueryableDocument)document;
        foreach (var collection in queryable.Collections.Where(collection => collection.EmptyRows is not null))
        {
            var shown = session.QueryCollection(collection.Id, new() { IncludeEmpty = true, Offset = (int)collection.TotalCount - 1 });
            Assert.Single(shown.Rows);
            Assert.Equal(collection.TotalCount, shown.FilteredCount);
            Assert.Equal(0, shown.HiddenEmptyCount);
        }
        var visible = ReadAll(queryable, "inventory", null);
        var excluded = visible[0].Key;
        var parameters = new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement(99) };
        foreach (var includeEmpty in new bool?[] { null, false, true })
        {
            var scope = new ActionScope { QueryScope = new("inventory", new() { IncludeEmpty = includeEmpty }, session.State.Revision)
                { ExcludedRowKeys = [excluded] } };
            var preview = session.PlanAction(InventoryPlanning.ActionId, parameters, scope);
            Assert.NotNull(preview.PlanId);
            Assert.Empty(preview.Plan.Errors);
            var keys = preview.Plan.Changes.Select(change => change.ValueId[..change.ValueId.LastIndexOf('/')])
                .Concat(preview.Plan.SkippedRows.Select(row => row.RowKey)).ToHashSet(StringComparer.Ordinal);
            var expected = ReadAll(queryable, "inventory", includeEmpty).Where(row => row.Key != excluded).Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
            Assert.True(keys.SetEquals(expected));
            if (includeEmpty != true)
                Assert.DoesNotContain(ReadAll(queryable, "inventory", true).Where(row => row.IsEmpty), row => keys.Contains(row.Key));
        }
        var emptyOnly = session.PlanAction(InventoryPlanning.ActionId, parameters, new()
        {
            QueryScope = new("inventory", new() { Text = "Empty slot" }, session.State.Revision),
        });
        Assert.Empty(emptyOnly.Plan.Errors);
        Assert.Empty(emptyOnly.Plan.Changes);
        Assert.Empty(emptyOnly.Plan.SkippedRows);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static List<TableRow> ReadAll(IQueryableDocument document, string id, bool? includeEmpty)
    {
        var rows = new List<TableRow>();
        QueryableCollectionPage page;
        do
        {
            page = document.Query(id, new() { Offset = rows.Count, Limit = 200, IncludeEmpty = includeEmpty });
            rows.AddRange(page.Rows);
            Assert.Equal(rows.Count, rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
            Assert.True(page.Rows.Count > 0 || rows.Count == page.FilteredCount);
        } while (rows.Count < page.FilteredCount);
        return rows;
    }

    private static (int Offset, int Stride, int Count, uint Marker) Layout(Ff7Schema schema, string collection) => collection switch
    {
        "inventory" => (schema.IsRemake ? 0x34DD8 : 0x4FA70, schema.IsRemake ? 24 : 32, 2048, uint.MaxValue),
        "materia" => (schema.IsRemake ? 0x1844 : 0xBFA6C, 32, 1000, schema.IsRemake ? 0U : uint.MaxValue),
        "owned-weapon" => (0x5FA70, 32, 128, uint.MaxValue),
        "owned-materia" => (0x60A70, 32, 1000, uint.MaxValue),
        _ => throw new ArgumentException("Not a fixed-slot collection.", nameof(collection)),
    };
}
