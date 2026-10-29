using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class EmptySlotSampleTests
{
    [Fact]
    public void OnlyVacantStoredEquipmentSlotsAreHiddenAndShowingThemPreservesEveryKey()
    {
        foreach (var file in ProgressionSampleTests.Files())
        {
            var container = CsavContainer.Parse(file.ReadAllBytes());
            var tree = CsavTree.Parse(container);
            var slots = EquipmentData.Read(new ScriptableReader(container, tree), container, tree, InventoryReader.Read(container, tree)).Slots;
            var plugin = new Cyberpunk2077Plugin();
            using var document = plugin.Open(file);
            using var session = new DocumentSession("empty-equipment", (DiskSaveFile)file, plugin, document);
            var view = Assert.IsType<TabsNode>(session.BuildView());
            var shell = Assert.Single(view.Tabs.Single(tab => tab.Title == "Equipment and cyberware").Children
                .OfType<QueryableTableNode>(), node => node.CollectionId == EquipmentInspection.CollectionId);
            var metadata = Assert.IsAssignableFrom<IQueryableDocument>(document).Collections.Single(c => c.Id == shell.CollectionId);
            Assert.Equal(new EmptyRowPresentation("item"), shell.EmptyRows);
            Assert.Equal(metadata.EmptyRows, shell.EmptyRows);

            var visible = session.QueryCollection(shell.CollectionId, new() { Limit = 200 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
            var shown = session.QueryCollection(shell.CollectionId, new() { IncludeEmpty = true, Limit = 200 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(slots.Count, shown.TotalCount);
            Assert.Equal(slots.Select(slot => slot.Key).Order(), shown.Rows.Select(row => row.Key));
            Assert.Equal(slots.Count(slot => slot.IsEmpty), visible.HiddenEmptyCount);
            Assert.True(visible.HiddenEmptyCount > 0);
            Assert.Equal(slots.Count(slot => !slot.IsEmpty), visible.FilteredCount);
            Assert.Equal(slots.Count, shown.FilteredCount);
            Assert.Equal(0, shown.HiddenEmptyCount);
            Assert.All(visible.Rows, row => Assert.False(row.IsEmpty));
            Assert.Contains(visible.Rows, row => row.Key == "equipment/RightArm/0"); // Occupied, unresolved instance.
            foreach (var slot in slots.Where(slot => slot.Index < 0))
            {
                var omitted = Assert.Single(visible.Rows, row => row.Key == slot.Key);
                Assert.False(omitted.IsEmpty);
                Assert.Equal("No stored slot array", ReadOnlyInspectionSampleTests.Cell(omitted, "item"));
            }
            Assert.All(shown.Rows.Where(row => row.IsEmpty), row =>
                Assert.Equal("Empty slot", ReadOnlyInspectionSampleTests.Cell(row, "item")));
            Assert.Equal(7, shown.Rows.Count(row => ReadOnlyInspectionSampleTests.Cell(row, "identity").Length > 0));
            var emptyMatch = session.QueryCollection(shell.CollectionId, new() { Text = "Empty slot", Limit = 200 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Empty(emptyMatch.Rows);
            Assert.Equal(visible.HiddenEmptyCount, emptyMatch.HiddenEmptyCount);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
            Assert.False(session.State.IsDirty);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void F6WholeQuerySelectionRetainsEquipmentVisibilityAndExclusions(bool? includeEmpty)
    {
        var file = ProgressionSampleTests.Files()[0];
        var plugin = new Cyberpunk2077Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("equipment-selection", (DiskSaveFile)file, plugin, document);
        _ = session.BuildView();
        var query = new QueryableCollectionQuery { IncludeEmpty = includeEmpty, Limit = 200 };
        var rows = session.QueryCollection(EquipmentInspection.CollectionId, query, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows;
        var excluded = rows[0].Key;
        var scope = new ActionScope { QueryScope = new(EquipmentInspection.CollectionId, query, session.State.Revision)
            { ExcludedRowKeys = [excluded] } };
        var preview = session.PlanAction(StackEditor.SetTarget,
            new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement("700") }, scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(includeEmpty == true, preview.Scope.QueryScope!.Query.IncludeEmpty);
        Assert.NotNull(preview.Scope.RowKeys);
        Assert.Equal(rows.Select(row => row.Key).Where(key => key != excluded), preview.Scope.RowKeys);
        Assert.DoesNotContain(excluded, preview.Scope.RowKeys);
        if (includeEmpty != true)
        {
            var empties = session.QueryCollection(EquipmentInspection.CollectionId, query with { IncludeEmpty = true }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows.Where(row => row.IsEmpty);
            Assert.NotEmpty(empties);
            Assert.All(empties, row => Assert.DoesNotContain(row.Key, preview.Scope.RowKeys));
        }
        // The scope is resolved, but inventory stack actions still refuse read-only equipment.
        Assert.Null(preview.PlanId);
        Assert.Contains(preview.Plan.Errors, error => error.Message.Contains("inventory scope", StringComparison.Ordinal));
        Assert.False(session.State.IsDirty);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(ulong.MaxValue)]
    [InlineData(0x12345678UL)]
    public void UnknownInventoryIdsAndZeroQuantitiesDoNotEstablishVacantSlots(ulong tweakId)
    {
        var original = CsavContainer.Parse(ProgressionSampleTests.Files()[0].ReadAllBytes());
        var node = CsavTree.Parse(original).Nodes[49];
        var payload = original.Payload.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(node.Offset - ItemIdentity.WireSize), tweakId);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(node.Offset + sizeof(int)), tweakId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(node.Offset + node.Size - sizeof(uint)), 0);
        var bytes = original.Rebuild(payload);
        using var document = ProgressionSampleTests.Open(bytes);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.Null(query.Collections.Single(c => c.Id == InventoryInspection.CollectionId).EmptyRows);
        var page = query.Query(InventoryInspection.CollectionId, new() { IncludeEmpty = false, Limit = 200 });
        var row = Assert.Single(page.Rows, row => row.Key == "49");
        Assert.False(row.IsEmpty);
        Assert.Equal($"0x{tweakId:X16}", Assert.IsType<TextNode>(row.Cells["hash"]).Text);
        Assert.Equal(0U, Assert.IsType<FieldNode>(row.Cells["quantity"]).Value.AsUInt32());
        Assert.Equal((22, 22, 0), (page.TotalCount, page.FilteredCount, page.HiddenEmptyCount));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(101, true)]
    [InlineData(128, true)]
    public void InventoryUsesExactlyOneTableAndSwitchesToF8AboveOneHundredRows(int count, bool queryable)
    {
        var inspection = InventoryInspectionFixture.Create(count);
        var view = inspection.Build();
        if (queryable)
        {
            Assert.Single(view.OfType<QueryableTableNode>());
            Assert.Empty(view.OfType<SectionNode>());
        }
        else
        {
            Assert.Single(view.OfType<SectionNode>());
            var table = Assert.IsType<TableNode>(Assert.Single(inspection.Expand()));
            Assert.Equal(count, table.Rows.Count);
            Assert.Equal(Enumerable.Range(0, count).Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).Order(),
                table.Rows.Select(row => row.Key).Order());
        }
    }
}
