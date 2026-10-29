using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithInventoryQueryTests
{
    private readonly FinalFantasy16Plugin _plugin = new();

    [Fact]
    public void CapacityQueriesPreserveLongLocalizedLabelsWithoutRequiringIcons()
    {
        const int RepetitionsWithinCatalogNameLimit = 40;
        var name = string.Concat(Enumerable.Repeat("長い項目名 Ä Ö Ü ", RepetitionsWithinCatalogNameLimit)).TrimEnd();
        var records = JsonSerializer.Serialize(new { records = new[] { new { id = "60002", name, category = "synthetic" } } });
        using var manifestStream = typeof(FinalFantasy16Plugin).Assembly.GetManifestResourceStream("refpacks/ffxvi.wiki.en/pack.json")!;
        var manifest = JsonNode.Parse(manifestStream)!;
        manifest["id"] = "ffxvi.test-long-name";
        manifest["version"] = "99.0.0";
        manifest["datasets"]![0]!["id"] = FaithCatalog.ItemsId;
        manifest["datasets"]![0]!["files"]![0]!["sha256"] = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(records)));
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("pack.json").Open())) writer.Write(manifest.ToJsonString());
            using (var writer = new StreamWriter(zip.CreateEntry("items.json").Open())) writer.Write(records);
        }
        var store = new ReferencePackStore([_plugin]);
        _ = store.Import(memory.ToArray(), "synthetic-long-label.zip");
        using var document = _plugin.Open(FaithTestData.File(CapacitySave()), store.Pin(_plugin, "PPSA10665"));
        var page = ((IQueryableDocument)document).Query(FaithInventoryView.CollectionId, new() { Text = "長い項目名" });
        Assert.Equal(1200, page.TotalCount);
        Assert.Equal(1, page.FilteredCount);
        Assert.Equal(name, Assert.IsType<TextNode>(Assert.Single(page.Rows).Cells["name"]).Text);
        Assert.Equal("60002", Assert.IsType<FieldNode>(page.Rows[0].Cells["id"]).Value.AsString());
        Assert.Null(store.Pin(_plugin, "PPSA10665").Catalog(FaithCatalog.ItemsId)!.Find("60002")!.AssetId);
    }

    [Fact]
    public void CapacityInventoryIsPagedSearchableAndNumericallySortedWithStableTies()
    {
        var bytes = CapacitySave();
        using var document = _plugin.Open(FaithTestData.File(bytes));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var view = Assert.IsType<TabsNode>(document.BuildView());
        var inventory = view.Tabs.Single(tab => tab.Title == "Inventory");
        Assert.Single(inventory.Children.OfType<QueryableTableNode>());
        Assert.DoesNotContain(FaithViewFields.All(document.BuildView()), field => field.ValueId == "inventory/1100/item");
        var rows = Enumerable.Range(0, 6).SelectMany(page => queryable.Query(FaithInventoryView.CollectionId,
            new QueryableCollectionQuery { Offset = page * 200, Limit = 200 }).Rows).ToArray();
        Assert.Equal(FaithInventory.Capacity, rows.Length);
        Assert.Equal(rows.Length, rows.Select(row => row.Key).Distinct().Count());
        Assert.Equal("inventory/10", rows[10].Key); // Numeric slot ordering, not lexicographic.
        var sorted = queryable.Query(FaithInventoryView.CollectionId,
            new QueryableCollectionQuery { SortColumn = "quantity", Descending = true, Limit = 2 });
        Assert.Equal(["inventory/1199", "inventory/1198"], sorted.Rows.Select(row => row.Key));
        var ties = queryable.Query(FaithInventoryView.CollectionId, new QueryableCollectionQuery
            { SortColumn = "category", Filters = new Dictionary<string, string> { ["category"] = "unmapped" }, Limit = 2 });
        Assert.Equal(["inventory/2", "inventory/3"], ties.Rows.Select(row => row.Key));
        var search = queryable.Query(FaithInventoryView.CollectionId,
            new QueryableCollectionQuery { Text = "Unknown item", Filters = new Dictionary<string, string> { ["id"] = "61100" } });
        Assert.Equal(FaithInventory.Capacity, search.TotalCount);
        Assert.Equal(1, search.FilteredCount);
        Assert.Equal("inventory/1100", Assert.Single(search.Rows).Key);
        Assert.IsType<SectionNode>(search.Rows[0].Cells["details"]);
        Assert.Equal(Confidence.Unknown, Assert.IsType<FieldNode>(search.Rows[0].Cells["id"]).Confidence);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void ZeroMatchesFullExclusionAndServerRowCapsNeverFallBackToDocumentScope()
    {
        var bytes = CapacitySave();
        using var fixture = new FaithSessionFile(bytes);
        using var document = _plugin.Open(fixture.File);
        using var session = new DocumentSession("query-empty", fixture.File, _plugin, document,
            new DocumentSessionOptions { MaxQueryActionRows = 100 });
        _ = session.BuildView();
        var parameters = new Dictionary<string, JsonElement> { [FaithQuantityPlanning.QuantityParameter] = JsonSerializer.SerializeToElement(1) };
        foreach (var queryScope in new ActionQueryScope[]
        {
            new(FaithInventoryView.CollectionId, new() { Text = "no-such-item" }, session.State.Revision),
            new(FaithInventoryView.CollectionId, new() { Filters = new Dictionary<string, string> { ["slot"] = "1100" } }, session.State.Revision)
                { ExcludedRowKeys = ["inventory/1100"] },
        })
        {
            var preview = session.PlanAction(FaithQuantityPlanning.ActionId, parameters, new ActionScope { QueryScope = queryScope });
            Assert.Empty(preview.Plan.Errors);
            Assert.Empty(preview.Plan.Changes);
            Assert.Empty(preview.Scope.RowKeys!);
            Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.False(session.State.CanUndo);
        }
        var tooMany = session.PlanAction(FaithQuantityPlanning.ActionId, parameters, new ActionScope
            { QueryScope = new(FaithInventoryView.CollectionId, new(), session.State.Revision) });
        Assert.Null(tooMany.PlanId);
        Assert.NotEmpty(tooMany.Plan.Errors);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void WholeFilteredActionCoversUnloadedRowsHonorsExclusionsAndUndoesExactly()
    {
        var bytes = CapacitySave();
        using var fixture = new FaithSessionFile(bytes);
        var file = fixture.File;
        using var document = _plugin.Open(file);
        using var session = new DocumentSession("capacity-query", file, _plugin, document);
        // Derive the oracle from the fixture's slot layout, independently of the query/planner/parser.
        const int PayloadOffset = 32;
        const int RecordSize = 16;
        const int QuantityOffset = sizeof(int);
        var selectedSlots = Enumerable.Range(2, 1198).Where(slot => slot != 1100).ToArray();
        var expected = bytes.ToArray();
        foreach (var slot in selectedSlots)
            BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(PayloadOffset + slot * RecordSize + QuantityOffset), 1);
        FaithTestData.FixCrc(expected);
        var expectedChanges = selectedSlots.Select(slot =>
            (ValueId: $"inventory/{slot}/quantity", OldValue: FieldValue.Of(slot + 2), NewValue: FieldValue.Of(1)))
            .Append((ValueId: "integrity/crc32", OldValue: FieldValue.Of((ulong)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4))),
                NewValue: FieldValue.Of((ulong)BinaryPrimitives.ReadUInt32LittleEndian(expected.AsSpan(4)))))
            .OrderBy(change => change.ValueId, StringComparer.Ordinal).ToArray();
        _ = session.BuildView();
        _ = session.QueryCollection(FaithInventoryView.CollectionId, new QueryableCollectionQuery { Limit = 1 });
        var parameters = new Dictionary<string, JsonElement> { [FaithQuantityPlanning.QuantityParameter] = JsonSerializer.SerializeToElement(1) };
        var scope = new ActionScope
        {
            QueryScope = new ActionQueryScope(FaithInventoryView.CollectionId, new QueryableCollectionQuery
                { Filters = new Dictionary<string, string> { ["category"] = "unmapped" }, SortColumn = "slot", Descending = true }, session.State.Revision)
                { ExcludedRowKeys = ["inventory/1100"] },
        };
        var preview = session.PlanAction(FaithQuantityPlanning.ActionId, parameters, scope);
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Plan.SkippedRows);
        Assert.Equal(selectedSlots.Select(slot => $"inventory/{slot}").Reverse(), preview.Scope.RowKeys!);
        Assert.Equal(expectedChanges, preview.Plan.Changes.Select(change => (change.ValueId, change.OldValue, change.NewValue))
            .OrderBy(change => change.ValueId, StringComparer.Ordinal));
        Assert.All(preview.Plan.Changes, change => Assert.Equal(change.ValueId == "integrity/crc32", change.Derived));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        var applied = session.ApplyActionPlan(preview.PlanId, preview.Revision);
        Assert.True(applied.Ok);
        Assert.Equal(expectedChanges, applied.Changes.Select(change => (change.ValueId, change.OldValue, change.NewValue))
            .OrderBy(change => change.ValueId, StringComparer.Ordinal));
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.Equal(expected, edited);
        using var reopened = _plugin.Open(FaithTestData.File(edited));
        for (var slot = 0; slot < FaithInventory.Capacity; slot++)
        {
            var quantity = slot >= 2 && slot != 1100 ? 1 : slot + 2;
            Assert.Equal(quantity, document.GetValue($"inventory/{slot}/quantity").AsInt32());
            Assert.Equal(quantity, reopened.GetValue($"inventory/{slot}/quantity").AsInt32());
        }
        Assert.True(session.Undo().Ok);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(session.State.CanUndo);
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.PlanAction(FaithQuantityPlanning.ActionId, parameters, scope).Stale);
    }

    [Fact]
    public void QueriesSeeIdentityAndQuantityChangesAfterUndoAndRejectInvalidRequests()
    {
        var bytes = CapacitySave();
        using var fixture = new FaithSessionFile(bytes);
        var file = fixture.File;
        using var document = _plugin.Open(file);
        using var session = new DocumentSession("query-refresh", file, _plugin, document);
        _ = session.BuildView();
        var query = new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["slot"] = "1100" } };
        var page = session.QueryCollection(FaithInventoryView.CollectionId, query);
        var result = session.ApplyEdits([new IncomingEdit("inventory/1100/item", JsonSerializer.SerializeToElement("5")),
            new IncomingEdit("inventory/1100/quantity", JsonSerializer.SerializeToElement(0))]);
        Assert.True(result.Ok);
        Assert.Equal("Potion", Assert.IsType<TextNode>(session.QueryCollection(FaithInventoryView.CollectionId, query).Rows[0].Cells["name"]).Text);
        Assert.True(session.Undo().Ok);
        Assert.Equal("Unknown item 61100", Assert.IsType<TextNode>(session.QueryCollection(FaithInventoryView.CollectionId, query).Rows[0].Cells["name"]).Text);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Throws<StaleViewException>(() => session.QueryCollection(FaithInventoryView.CollectionId, query, page.Revision));
        var queryable = (IQueryableDocument)document;
        Assert.Throws<KeyNotFoundException>(() => queryable.Query("secondary", query));
        foreach (var bad in new QueryableCollectionQuery[] { new() { Cursor = "foreign" }, new() { SortColumn = "missing" },
            new() { Filters = new Dictionary<string, string> { ["missing"] = "x" } }, new() { Text = new string('x', 257) } })
            Assert.Throws<ArgumentException>(() => queryable.Query(FaithInventoryView.CollectionId, bad));
        var planner = (IActionPlanningDocument)document;
        var parameters = new Dictionary<string, FieldValue> { [FaithQuantityPlanning.QuantityParameter] = FieldValue.Of(1) };
        Assert.NotEmpty(planner.PlanAction(FaithQuantityPlanning.ActionId, parameters, new ActionScope
            { QueryScope = new(FaithInventoryView.CollectionId, new(), 0) }).Errors);
        Assert.NotEmpty(planner.PlanAction(FaithQuantityPlanning.ActionId, parameters, new ActionScope(["inventory/1"])
            { QueryScope = new("secondary", new(), 0) }).Errors);
    }

    [Theory]
    [InlineData("PPSA10665", 172)]
    [InlineData("PPSA13803", 2)]
    public void RealSampleQueriesMatchEveryParsedRowWithoutMutating(string title, int count)
    {
        var file = FaithSamples.Snapshot(title);
        using var document = _plugin.Open(file);
        _ = document.BuildView();
        var queryable = (IQueryableDocument)document;
        var page = queryable.Query(FaithInventoryView.CollectionId, new() { Limit = 200 });
        Assert.Equal(count, page.TotalCount);
        Assert.Equal(count, page.Rows.Count);
        var items = FaithInventory.Parse(file.ReadAllBytes(), FaithContainer.Parse(file.ReadAllBytes())).Items;
        Assert.Equal(items.Select(item => $"inventory/{item.Slot}"), page.Rows.Select(row => row.Key));
        Assert.All(page.Rows, row => Assert.Equal(document.GetValue($"{row.Key}/item"), Assert.IsType<FieldNode>(row.Cells["id"]).Value));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static byte[] CapacitySave()
    {
        var payload = new byte[FaithSchema.ItemLength];
        const int Metadata = FaithInventory.Capacity * 20;
        const int Keys = Metadata + 16;
        for (var slot = 0; slot < FaithInventory.Capacity; slot++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(slot * 16), slot switch { 0 => 1, 1 => 3, _ => 60000 + slot });
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(slot * 16 + sizeof(int)), slot + 2);
            BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(Keys + slot * sizeof(ulong)), (ulong)slot << 32 | (uint)slot);
        }
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(Metadata), FaithInventory.Capacity);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(Metadata + sizeof(ulong)), FaithInventory.Capacity);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(Keys + FaithInventory.Capacity * sizeof(ulong)), FaithInventory.Capacity);
        return FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, payload));
    }
}
