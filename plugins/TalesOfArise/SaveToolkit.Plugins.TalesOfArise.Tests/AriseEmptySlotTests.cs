using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseEmptySlotTests
{
    private const int SlotCount = 934;
    private const int RecordSize = 16;
    private const int ItemIdOffset = 2;
    private const int AppleSlot = 24;
    private const string AppleKey = "inventory/24/quantity";
    private readonly TalesOfArisePlugin _plugin = new();

    [Fact]
    public void EverySampleUsesOneInventoryTableAndPreservesAllSlotsAndBytes()
    {
        foreach (var file in AriseSampleTests.GameplayFiles())
        {
            using var document = _plugin.Open(file);
            var root = Assert.IsType<TabsNode>(document.BuildView());
            var tab = root.Tabs.Single(tab => tab.Title == "Inventory");
            var section = Assert.Single(tab.Children.OfType<SectionNode>());
            Assert.Equal("inventory", section.LazyId);
            var table = Assert.IsType<QueryableTableNode>(Assert.Single(document.ExpandNode(section.LazyId!)));
            var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
            var metadata = queryable.Collections.Single(collection => collection.Id == "inventory");
            Assert.Equal(SlotCount, metadata.TotalCount);
            Assert.Equal(metadata.TotalCount, table.TotalCount);
            Assert.Equal(metadata.EmptyRows, table.EmptyRows);
            Assert.Equal("name", table.EmptyRows!.LabelColumnKey);
            Assert.True(table.EmptyRows.HideByDefault);
            Assert.DoesNotContain("Slots ", JsonSerializer.Serialize(root), StringComparison.Ordinal);

            var save = AriseFormat.Decode(file.ReadAllBytes());
            var inventory = save.Sections.Single(section => section.Name == "SaveData_ItemManager");
            var occupiedCount = file.FileName == "SaveData_002.sav" ? 25 : 24;
            var visible = queryable.Query("inventory", new());
            Assert.Equal(occupiedCount, visible.FilteredCount);
            Assert.Equal(SlotCount - occupiedCount, visible.HiddenEmptyCount);
            Assert.Equal(occupiedCount, visible.Rows.Count);
            Assert.All(visible.Rows, row => Assert.False(row.IsEmpty));

            var all = new List<TableRow>();
            for (var offset = 0; offset < SlotCount; offset += ReferenceQuery.MaxLimit)
            {
                var page = queryable.Query("inventory", new() { IncludeEmpty = true, Offset = offset, Limit = ReferenceQuery.MaxLimit });
                Assert.Equal(SlotCount, page.FilteredCount);
                Assert.Equal(0, page.HiddenEmptyCount);
                Assert.InRange(page.Rows.Count, 1, ReferenceQuery.MaxLimit);
                all.AddRange(page.Rows);
            }

            Assert.Equal(SlotCount, all.Count);
            Assert.Equal(SlotCount, all.Select(row => row.Key).Distinct().Count());
            foreach (var index in Enumerable.Range(0, SlotCount))
            {
                var row = all.Single(row => row.Key == $"inventory/{index}/quantity");
                var bytes = save.Payload.AsSpan(inventory.Offset + index * RecordSize, RecordSize);
                var id = BinaryPrimitives.ReadUInt16LittleEndian(bytes[ItemIdOffset..]);
                Assert.Equal(id == 0, row.IsEmpty);
                Assert.Equal(id.ToString(), Assert.IsType<FieldNode>(row.Cells["id"]).Value.AsString());
                Assert.Equal(bytes[0], Assert.IsType<FieldNode>(row.Cells["quantity"]).Value.AsInt64());
                if (row.IsEmpty)
                {
                    Assert.All(bytes.ToArray(), value => Assert.Equal(0, value));
                    Assert.Equal("Empty slot", Assert.IsType<FieldNode>(row.Cells["name"]).Value.AsString());
                    Assert.True(Assert.IsType<FieldNode>(row.Cells["quantity"]).Spec.ReadOnly);
                }
            }

            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void FiltersAndVisibilityAreAppliedBeforePagingAndHiddenCounts()
    {
        using var document = _plugin.Open(Sample());
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var hidden = queryable.Query("inventory", new() { Text = "Empty slot", Offset = 700, Limit = 2 });
        Assert.Empty(hidden.Rows);
        Assert.Equal(0, hidden.FilteredCount);
        Assert.Equal(SlotCount - 25, hidden.HiddenEmptyCount);
        var shown = queryable.Query("inventory", new() { Text = "Empty slot", IncludeEmpty = true, Offset = 700, Limit = 2 });
        Assert.Equal(2, shown.Rows.Count);
        Assert.All(shown.Rows, row => Assert.True(row.IsEmpty));
        Assert.Equal(SlotCount - 25, shown.FilteredCount);
        Assert.Equal(0, shown.HiddenEmptyCount);
        var filter = new Dictionary<string, string> { ["id"] = "0" };
        var exact = queryable.Query("inventory", new() { Filters = filter });
        Assert.Empty(exact.Rows);
        Assert.Equal(SlotCount - 25, exact.HiddenEmptyCount);
        Assert.Equal(0, queryable.Query("inventory", new() { Text = "Apple Gel" }).HiddenEmptyCount);
        var forward = queryable.Query("inventory", new() { SortColumn = "name", Limit = 3 });
        var reverse = queryable.Query("inventory", new() { SortColumn = "name", Descending = true, Offset = 22, Limit = 3 });
        Assert.Equal(forward.Rows.Select(row => Assert.IsType<FieldNode>(row.Cells["name"]).Value.AsString()),
            reverse.Rows.Reverse().Select(row => Assert.IsType<FieldNode>(row.Cells["name"]).Value.AsString()));
    }

    [Fact]
    public void ZeroQuantityAndUnknownNonzeroIdsRemainVisibleAndEmptyRawBytesArePreserved()
    {
        var file = Sample();
        var save = AriseFormat.Decode(file.ReadAllBytes());
        var start = save.Sections.Single(section => section.Name == "SaveData_ItemManager").Offset;
        save.Payload[start + AppleSlot * RecordSize] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(save.Payload.AsSpan(start + ItemIdOffset), ushort.MaxValue);
        var emptyOffset = start + 25 * RecordSize;
        save.Payload[emptyOffset] = 7;
        save.Payload[emptyOffset + 1] = 0x80;
        var bytes = AriseFormat.Encode(save);
        using var document = _plugin.Open(MemorySaveSet.SingleFile(file.FileName, bytes, "PPSA02745"));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var visible = queryable.Query("inventory", new());
        Assert.Contains(visible.Rows, row => row.Key == AppleKey && !row.IsEmpty);
        Assert.Contains(visible.Rows, row => row.Key == "inventory/0/quantity" && !row.IsEmpty);
        Assert.Equal(25, visible.FilteredCount);
        var empty = Assert.Single(queryable.Query("inventory", new() { IncludeEmpty = true, Text = "Empty slot", Limit = 1 }).Rows);
        Assert.True(empty.IsEmpty);
        Assert.Equal("inventory/25/quantity", empty.Key);
        Assert.Equal(7, Assert.IsType<FieldNode>(empty.Cells["quantity"]).Value.AsInt64());
        Assert.Equal("0x80", Assert.IsType<TextNode>(empty.Cells["flags"]).Text);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void VisiblePagedQuantityEditsTheOriginalSlotAndSupportsUndoRedo()
    {
        var file = Sample();
        using var document = _plugin.Open(file);
        using var session = new DocumentSession("arise-f14-edit", file, _plugin, document);
        _ = session.BuildView();
        _ = session.Expand("inventory");
        var page = session.QueryCollection("inventory", new() { Text = "Apple Gel" });
        var row = Assert.Single(page.Rows);
        Assert.Equal(AppleKey, row.Key);
        Assert.False(row.IsEmpty);
        var result = session.ApplyEdits([new(AppleKey, JsonSerializer.SerializeToElement("12"))], page.Revision);
        Assert.True(result.Ok);
        var original = AriseFormat.Decode(file.ReadAllBytes());
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        var decoded = AriseFormat.Decode(edited);
        var quantityOffset = original.Sections.Single(section => section.Name == "SaveData_ItemManager").Offset + AppleSlot * RecordSize;
        Assert.Equal(12, decoded.Payload[quantityOffset]);
        decoded.Payload[quantityOffset] = original.Payload[quantityOffset];
        // The payload header includes the ciphertext digest, which changes with any edit.
        var cipherHeaderSize = AriseFormat.CipherOffset - AriseFormat.PayloadOffset;
        Assert.Equal(original.Payload.AsSpan(cipherHeaderSize).ToArray(), decoded.Payload.AsSpan(cipherHeaderSize).ToArray());
        Assert.Equal(file.ReadAllBytes().AsSpan(0, AriseFormat.PayloadOffset).ToArray(), edited.AsSpan(0, AriseFormat.PayloadOffset).ToArray());
        using var reopened = _plugin.Open(MemorySaveSet.SingleFile(file.FileName, edited, "PPSA02745"));
        Assert.Equal(12, Assert.IsType<FieldNode>(Assert.Single(reopened.LoadInventory().Rows, row => row.Key == AppleKey).Cells["quantity"]).Value.AsInt64());
        Assert.True(session.Undo().Ok);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(null, 25)]
    [InlineData(false, 25)]
    [InlineData(true, SlotCount)]
    public void F6SelectAllMatchingRetainsVisibilityAndExclusions(bool? includeEmpty, int count)
    {
        var file = Sample();
        using var document = _plugin.Open(file);
        using var session = new DocumentSession("arise-f14-plan", file, _plugin, document);
        _ = session.BuildView();
        var scope = new ActionScope
        {
            QueryScope = new ActionQueryScope("inventory", new() { IncludeEmpty = includeEmpty, Limit = 2 }, session.State.Revision)
            {
                ExcludedRowKeys = ["inventory/0/quantity"],
            },
        };
        var preview = session.PlanAction(AriseInventoryActions.SetQuantityAction,
            new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement("12") }, scope);
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(count - 1, preview.Scope.RowKeys!.Count);
        Assert.Equal(includeEmpty ?? false, preview.Scope.QueryScope!.Query.IncludeEmpty);
        Assert.DoesNotContain("inventory/0/quantity", preview.Scope.RowKeys);
        var emptyKeys = ((AriseDocument)document).ReadInventoryRows().Where(row => row.IsEmpty).Select(AriseInventoryActions.RowKey).ToHashSet();
        Assert.Equal(includeEmpty == true ? SlotCount - 25 : 0, preview.Scope.RowKeys.Count(emptyKeys.Contains));
        Assert.DoesNotContain(preview.Plan.Changes, change => emptyKeys.Contains(change.ValueId));
        Assert.Equal(includeEmpty == true ? SlotCount - 25 : 0, preview.Plan.SkippedRows.Count(row => emptyKeys.Contains(row.RowKey)));
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.True(session.Undo().Ok);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static DiskSaveFile Sample() => Assert.IsType<DiskSaveFile>(AriseSampleTests.GameplayFiles()
        .Single(file => file.FileName == "SaveData_002.sav" && !file.Directory.IsBackup));
}
