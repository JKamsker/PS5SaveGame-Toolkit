using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class LargeBankTests(ITestOutputHelper output)
{
    private const int ItemCount = 5000;
    // Observed ceilings: 7 MiB for a fresh 5,000-item view/page and 115 KiB for a repeated page.
    private const long ViewAndPageAllocationLimit = 12 * 1024 * 1024;
    private const long CachedPageAllocationLimit = 256 * 1024;

    [Theory]
    [InlineData("PPSA01463", true)]
    [InlineData("PPSA01493", true)]
    [InlineData("PPSA01463", false)]
    [InlineData("PPSA01493", false)]
    public void LargeBank_PagesSearchesAndCachesWithoutChangingDuplicateIdentities(string title, bool sampleBacked)
    {
        var plugin = new GearboxPlugin();
        var wonderlands = title == "PPSA01493";
        var file = sampleBacked
            ? SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == "profile" && !f.Directory.IsBackup)
            : GearboxTestData.File(title, GearboxTestData.CreateContainer(wonderlands, true), "profile");
        var container = GearboxContainer.Parse(file.ReadAllBytes(), title, true);
        var root = container.Decode();
        var source = sampleBacked ? SampleItem(title) :
            (Serial: IndependentSerialProbe.Encode(SyntheticItem.Payload(wonderlands), wonderlands ? 5 : 4, -1), Message: (ProtoMessage?)null);
        var spec = root.Schema.Spec(root.Type, GearboxInventory.Bank);
        var message = root.Schema.IsMessage(spec.Type) ? source.Message ?? new ProtoMessage(root.Schema, spec.Type, []) : null;
        if (message is not null && message.Last("item_serial_number") is null)
        {
            var serialSpec = root.Schema.Spec(message.Type, "item_serial_number");
            message.Add(new(root.Schema.Tag(message.Type, "item_serial_number"), 2, serialSpec, [], source.Serial, null));
        }
        var template = new ProtoField(root.Schema.Tag(root.Type, GearboxInventory.Bank), 2, spec, [], source.Serial, message);
        foreach (var field in root.All(GearboxInventory.Bank).ToArray()) root.Remove(field);
        for (var index = 0; index < ItemCount; index++) root.Add(template.Clone());
        var original = container.Encode(root);
        var overlaid = GearboxTestData.Overlay(file, original);
        var query = new QueryableCollectionQuery { Offset = 4000, Limit = 3, SortColumn = "index" };
        // Exclude one-time embedded catalog loading; the measured document still has fresh caches.
        using (var warmup = (GearboxDocument)plugin.Open(overlaid))
        {
            _ = warmup.BuildView();
            _ = warmup.Query(GearboxInventory.Bank, query);
        }
        GearboxDocument current = null!;
        var viewBytes = AllocationProbe.Measure(() =>
        {
            _ = current.BuildView();
            return current.Query(GearboxInventory.Bank, query);
        }, out var page, beforeRun: () =>
        {
            current?.Dispose();
            current = (GearboxDocument)plugin.Open(overlaid);
        });
        using var document = current;
        output.WriteLine($"{title} ({(sampleBacked ? "sample" : "synthetic")}): view + page allocated {viewBytes.MinimumBytes:N0} bytes.");
        viewBytes.AssertLessThan(ViewAndPageAllocationLimit, $"View + page allocated {viewBytes.MinimumBytes:N0} bytes.");
        Assert.Equal(ItemCount, page.TotalCount);
        Assert.Equal(3, page.Rows.Count);
        Assert.Equal("4000", Assert.IsType<TextNode>(page.Rows[0].Cells["index"]).Text);
        Assert.Equal(3, page.Rows.Select(row => row.Key).Distinct().Count());
        var pageBytes = AllocationProbe.Measure(() => document.Query(GearboxInventory.Bank, query), out var repeated);
        output.WriteLine($"Repeated page allocated {pageBytes.MinimumBytes:N0} bytes.");
        pageBytes.AssertLessThan(CachedPageAllocationLimit, $"Repeated page allocated {pageBytes.MinimumBytes:N0} bytes.");
        Assert.Equal(page.Rows.Select(row => row.Key), repeated.Rows.Select(row => row.Key));
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

    private static (byte[] Serial, ProtoMessage? Message) SampleItem(string title)
    {
        var file = GearboxTestData.PrimaryCharacter(title);
        using var character = (GearboxDocument)new GearboxPlugin().Open(file);
        var entry = character.Inventory.Entries.First(entry => character.Inventory.Info(entry.Id).Complete);
        return (entry.Serial, entry.Field.Message?.Clone());
    }
}
