using System.Diagnostics;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class LargeBankTests(ITestOutputHelper output)
{
    private const int ItemCount = 5000;

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void LargeBank_PagesSearchesAndCachesWithoutChangingDuplicateIdentities(string title)
    {
        var plugin = new GearboxPlugin();
        var file = SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == "profile" && !f.Directory.IsBackup);
        var container = GearboxContainer.Parse(file.ReadAllBytes(), title, true);
        var root = container.Decode();
        var characterFile = SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == "1" && !f.Directory.IsBackup);
        using var character = (GearboxDocument)plugin.Open(characterFile);
        var source = character.Inventory.Entries.First(entry => character.Inventory.Info(entry.Id).Complete);
        var spec = root.Schema.Spec(root.Type, GearboxInventory.Bank);
        var template = new ProtoField(root.Schema.Tag(root.Type, GearboxInventory.Bank), 2, spec, [], source.Serial,
            root.Schema.IsMessage(spec.Type) ? source.Field.Message!.Clone() : null);
        foreach (var field in root.All(GearboxInventory.Bank).ToArray()) root.Remove(field);
        for (var index = 0; index < ItemCount; index++) root.Add(template.Clone());
        var original = container.Encode(root);
        using var document = (GearboxDocument)plugin.Open(SerialWriteTests.Overlay(file, original));
        var timer = Stopwatch.StartNew();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        _ = document.BuildView();
        var page = document.Query(GearboxInventory.Bank, new() { Offset = 4000, Limit = 3, SortColumn = "index" });
        Assert.Equal(ItemCount, page.TotalCount);
        Assert.Equal(3, page.Rows.Count);
        Assert.Equal("4000", Assert.IsType<TextNode>(page.Rows[0].Cells["index"]).Text);
        Assert.Equal(3, page.Rows.Select(row => row.Key).Distinct().Count());
        output.WriteLine($"{title}: {ItemCount}-item bank initial view + middle page: {timer.Elapsed.TotalMilliseconds:F1} ms, " +
            $"{GC.GetAllocatedBytesForCurrentThread() - allocated} thread allocation bytes (measurement only; no timing assertion).");
        var ids = page.Rows.Select(row => row.Key).ToArray();
        foreach (var id in ids)
        {
            Assert.Same(document.Inventory.Find(id), document.Inventory.Find(id));
            Assert.Same(document.Inventory.Info(id), document.Inventory.Info(id));
        }
        var name = Assert.IsType<FieldNode>(page.Rows[0].Cells["name"]).Value.AsString();
        var matching = document.Query(GearboxInventory.Bank, new() { Text = name, Offset = 4000, Limit = 3, SortColumn = "index" });
        Assert.Equal(ItemCount, matching.FilteredCount);
        Assert.Equal(ids, matching.Rows.Select(row => row.Key));
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }
}
