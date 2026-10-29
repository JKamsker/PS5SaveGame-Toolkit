using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class EmptySlotTests : IDisposable
{
    private readonly string _scratch = Path.Combine(TestScratch.ProcessFolder, "GearboxF14-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_scratch)) Directory.Delete(_scratch, recursive: true);
    }

    // DocumentSession requires a DiskSaveFile. The shared process folder also cleans up on exit.
    private DiskSaveFile Import(string title, byte[] bytes) => SaveSetImporter.Import(
        [new ImportSource(title + "/sdimg_F14/1", () => new MemoryStream(bytes))], _scratch).AllDiskFiles.Single();

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void SampleEquipmentSentinels_AreMarkedWithoutChangingKeysCellsOrBytes(string title)
    {
        var files = SampleSaves.Load(title).AllDiskFiles.Where(f => f.FileName == "1").ToArray();
        Assert.Equal(6, files.Length);
        var emptyCount = 0;
        foreach (var file in files)
        {
            using var document = (GearboxDocument)new GearboxPlugin().Open(file);
            _ = document.BuildView();
            var table = Assert.Single(document.ExpandNode("slots").OfType<TableNode>());
            Assert.Equal("slot", table.EmptyRows!.LabelColumnKey);
            Assert.True(table.EmptyRows.HideByDefault);
            var slots = document.Root.Children("equipped_inventory_list").ToArray();
            Assert.Equal(slots.Length, table.Rows.Count);
            for (var i = 0; i < slots.Length; i++)
            {
                var index = slots[i].Integer("inventory_list_index");
                var rawIndex = IndependentSaveProbe.Records(slots[i].Encode()).Where(r => r.Tag == 1)
                    .Select(r => unchecked((int)IndependentSaveProbe.Scalar(r.Data))).LastOrDefault();
                Assert.Equal(index, rawIndex);
                Assert.Equal(i.ToString(CultureInfo.InvariantCulture), table.Rows[i].Key);
                Assert.Equal(index == -1, table.Rows[i].IsEmpty);
                Assert.Equal(index.ToString(CultureInfo.InvariantCulture), Assert.IsType<TextNode>(table.Rows[i].Cells["index"]).Text);
                Assert.Equal(slots[i].Text("slot_data_path"), Assert.IsType<FieldNode>(table.Rows[i].Cells["slot"]).Value.AsString());
            }
            var hidden = document.Query("slots", new());
            Assert.Equal(table.Rows.Count(row => row.IsEmpty), hidden.HiddenEmptyCount);
            Assert.All(hidden.Rows, row => Assert.False(row.IsEmpty));
            emptyCount += table.Rows.Count(row => row.IsEmpty);
            Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        }
        Assert.True(emptyCount > 0, "The sample corpus must confirm the vacant equipment sentinel.");
    }

    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, 205)]
    public void EquipmentQueries_FilterVisibilityBeforePagingAndResolveF6Selection(bool wonderlands, int count)
    {
        var title = wonderlands ? "PPSA01493" : "PPSA01463";
        var container = GearboxContainer.Parse(GearboxTestData.CreateContainer(wonderlands, false), title, false);
        var root = container.Decode();
        for (var i = 0; i < count; i++)
        {
            var slot = GearboxTestData.AddMessage(root, "equipped_inventory_list");
            // Only -1 is confirmed vacant: zero, disabled slots and unknown negatives remain visible.
            slot.SetInteger("inventory_list_index", i % 3 == 0 ? -1 : i == 2 ? -2 : 0);
            slot.SetInteger("enabled", 0);
            slot.SetText("slot_data_path", "Synthetic/slot/" + i);
        }
        var bytes = container.Encode(root);
        var file = Import(title, bytes);
        var plugin = new GearboxPlugin();
        using var document = (GearboxDocument)plugin.Open(file);
        using var session = new DocumentSession("empty-slots", file, plugin, document);
        _ = session.BuildView();
        var nodes = session.Expand("slots");
        Assert.Single(nodes);
        if (count > 100)
        {
            var table = Assert.IsType<QueryableTableNode>(nodes[0]);
            Assert.Equal(document.Collections.Single(c => c.Id == "slots").EmptyRows, table.EmptyRows);
        }
        else Assert.IsType<TableNode>(nodes[0]);
        var all = document.Query("slots", new() { IncludeEmpty = true, Limit = 200 });
        Assert.Equal(count, all.FilteredCount);
        Assert.Equal(0, all.HiddenEmptyCount);
        Assert.Contains(all.Rows, row => row.IsEmpty);
        var expected = Enumerable.Range(0, count).Where(i => i % 3 != 0).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        var hidden = session.QueryCollection("slots", new() { Limit = 2, Offset = 1 });
        Assert.Equal(count - expected.Length, hidden.HiddenEmptyCount);
        Assert.Equal(expected.Length, hidden.FilteredCount);
        Assert.Equal(expected.Skip(1).Take(2), hidden.Rows.Select(r => r.Key));
        var text = document.Query("slots", new() { Text = "Empty slot" });
        Assert.Empty(text.Rows);
        Assert.Equal(count - expected.Length, text.HiddenEmptyCount);
        var shown = document.Query("slots", new() { Text = "Empty slot", IncludeEmpty = true, Limit = 200 });
        Assert.All(shown.Rows, row => Assert.True(row.IsEmpty));
        Assert.Equal(count - expected.Length, shown.FilteredCount);
        var filtered = document.Query("slots", new() { Filters = new Dictionary<string, string> { ["index"] = "-1" } });
        Assert.Empty(filtered.Rows);
        Assert.Equal(count - expected.Length, filtered.HiddenEmptyCount);
        var parameters = new Dictionary<string, JsonElement> { ["level"] = JsonSerializer.SerializeToElement("17") };
        var preview = session.PlanAction("items/level", parameters, new ActionScope
        { QueryScope = new("slots", new() { Limit = 1 }, session.State.Revision) });
        Assert.Equal(expected, preview.Scope.RowKeys);
        Assert.False(preview.Scope.QueryScope!.Query.IncludeEmpty);
        // Equipment rows are not item identities: the item writer must still reject this scope.
        Assert.NotEmpty(preview.Plan.Errors);
        var shownSelection = session.PlanAction("items/level", parameters, new ActionScope
        { QueryScope = new("slots", new() { IncludeEmpty = true, Limit = 1 }, session.State.Revision) { ExcludedRowKeys = ["0"] } });
        Assert.Equal(count - 1, shownSelection.Scope.RowKeys!.Count);
        Assert.DoesNotContain("0", shownSelection.Scope.RowKeys);
        Assert.Equal(Enumerable.Range(1, count - 1).Select(i => i.ToString(CultureInfo.InvariantCulture)), shownSelection.Scope.RowKeys);
        var sorted = document.Query("slots", new() { SortColumn = "index", Descending = true, Limit = 200 });
        Assert.Equal("2", sorted.Rows[^1].Key);
        Assert.All(sorted.Rows, row => Assert.False(row.IsEmpty));
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargeAmmoList_IsOneSearchableTableAndVisibleEditWritesItsOriginalRecord(bool wonderlands)
    {
        var title = wonderlands ? "PPSA01493" : "PPSA01463";
        var container = GearboxContainer.Parse(GearboxTestData.CreateContainer(wonderlands, false), title, false);
        var root = container.Decode();
        for (var i = 0; i < 205; i++)
        {
            var pool = GearboxTestData.AddMessage(root, "resource_pools");
            pool.SetText("resource_path", "Synthetic/pool/" + i);
            pool.SetFloat("amount", i);
        }
        var original = container.Encode(root);
        var file = Import(title, original);
        var plugin = new GearboxPlugin();
        using var document = (GearboxDocument)plugin.Open(file);
        using var session = new DocumentSession("large-ammo", file, plugin, document);
        _ = session.BuildView();
        Assert.IsType<QueryableTableNode>(Assert.Single(session.Expand("ammo")));
        var page = session.QueryCollection("ammo", new() { Text = "Synthetic/pool/204" });
        var row = Assert.Single(page.Rows);
        Assert.Equal("204", row.Key);
        var field = Assert.IsType<FieldNode>(row.Cells["amount"]);
        Assert.Equal("ammo/204", field.ValueId);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.True(session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement(321))]).Ok);
        var edited = document.Serialize().Single().Content.ToArray();
        var reopened = GearboxContainer.Parse(edited, title, false).Decode();
        Assert.Equal(321, reopened.Children("resource_pools").ElementAt(204).Float("amount"));
        Assert.Equal(root.Children("resource_pools").Take(204).Select(m => Convert.ToHexString(m.Encode())),
            reopened.Children("resource_pools").Take(204).Select(m => Convert.ToHexString(m.Encode())));
        SerialWriteTests.AssertUnrelated(root, reopened, "resource_pools");
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, document.Serialize().Single().Content.ToArray());
        var zero = document.Query("ammo", new() { Filters = new Dictionary<string, string> { ["amount"] = "0" } });
        Assert.False(Assert.Single(zero.Rows).IsEmpty);
    }
}
