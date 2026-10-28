using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class ReadOnlyInspectionSampleTests(ITestOutputHelper output)
{
    [Fact]
    public void OwnerLoadoutRequiresAllIdentityMembersAndShowsUnresolvedSpecialSlots()
    {
        foreach (var file in ProgressionSampleTests.Files())
        {
            var container = CsavContainer.Parse(file.ReadAllBytes());
            var tree = CsavTree.Parse(container);
            var items = InventoryReader.Read(container, tree);
            var reader = new ScriptableReader(container, tree);
            var equipment = EquipmentData.Read(reader, container, tree, items);
            Assert.Equal(36, equipment.Slots.Select(s => s.Area).Distinct().Count());
            Assert.Equal(7, equipment.Slots.Count(s => s.Item is not null));
            Assert.Equal(6, equipment.Slots.Count(s => s.Inventory is not null));
            Assert.All(equipment.Slots.Where(s => s.Cyberware), slot => Assert.Null(slot.Item));
            Assert.Contains(equipment.Slots, slot => slot.Area == "RightArm" && slot.Item is not null && slot.Inventory is null);
            var shirt = Assert.Single(equipment.Slots, slot => slot.Area == "InnerChest");
            Assert.Equal(55, shirt.Inventory!.NodeId);
            Assert.Contains("InnerChest", equipment.State(55));

            // A duplicate base ID in the same player inventory must never steal a slot.
            var duplicate = items.Single(item => item.NodeId == 56) with { TweakId = shirt.Item!.TweakId };
            var changed = container.Payload.ToArray();
            var shirtOffset = tree.Nodes[55].Offset + 4;
            var duplicateOffset = tree.Nodes[56].Offset + 4;
            changed.AsSpan(shirtOffset, 8).CopyTo(changed.AsSpan(duplicateOffset));
            changed.AsSpan(shirtOffset, 8).CopyTo(changed.AsSpan(tree.Nodes[56].Offset - 16));
            var edited = CsavContainer.Parse(container.Rebuild(changed));
            var joined = EquipmentData.Read(new ScriptableReader(edited, tree), edited, tree,
                items.Select(item => item.NodeId == duplicate.NodeId ? duplicate : item).ToArray());
            Assert.Equal(55, Assert.Single(joined.Slots, slot => slot.Area == "InnerChest").Inventory!.NodeId);
        }
    }

    [Fact]
    public void MergedFactsCoverCatalogAndSavedUnknownsWithoutTreatingAbsenceAsZero()
    {
        var file = ProgressionSampleTests.Files()[0];
        var (plugin, _, context) = ReferencePackFixture.Embedded();
        var timer = Stopwatch.StartNew();
        using var document = plugin.Open(file, context);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        _ = document.BuildView();
        output.WriteLine($"Open + view: {timer.Elapsed.TotalMilliseconds:F1} ms");
        var metadata = query.Collections.Single(c => c.Id == FactCatalogInspection.CollectionId);
        Assert.Equal(20978, metadata.TotalCount);
        var absent = query.Query(metadata.Id, new QueryableCollectionQuery { Filters = new Dictionary<string, string>
            { ["presence"] = "Catalog only / absent from save" }, Limit = 200 });
        Assert.Equal(18228, absent.FilteredCount);
        Assert.All(absent.Rows, row => { Assert.Equal("", Cell(row, "value")); Assert.Equal("", Cell(row, "table")); });
        var zero = query.Query(metadata.Id, new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["value"] = "0" }, Limit = 200 });
        Assert.NotEmpty(zero.Rows);
        Assert.All(zero.Rows, row => Assert.Equal("Present in save", Cell(row, "presence")));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset < metadata.TotalCount; offset += 200)
        {
            var page = query.Query(metadata.Id, new QueryableCollectionQuery { Offset = offset, Limit = 200 });
            Assert.InRange(page.Rows.Count, 1, 200);
            foreach (var row in page.Rows) Assert.True(keys.Add(row.Key));
        }
        Assert.Equal(metadata.TotalCount, keys.Count);
        var canonical = ReferencePackFixture.All(context.Catalog("facts", "2210")!).Select(record => record.Attributes["canonicalName"])
            .First(name => name != name.Trim());
        var significant = query.Query(metadata.Id, new QueryableCollectionQuery { Text = canonical, Limit = 200 });
        Assert.Contains(significant.Rows, row => Cell(row, "name") == canonical);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void ProgressionWorldAndModifierReadersAgreeWithTheEarlyGameBytes()
    {
        foreach (var file in ProgressionSampleTests.Files())
        {
            var container = CsavContainer.Parse(file.ReadAllBytes());
            var tree = CsavTree.Parse(container);
            var reader = new ScriptableReader(container, tree);
            var development = new DevelopmentInspection(reader, null);
            Assert.Null(development.Warning);
            var all = development.Table.Query(new QueryableCollectionQuery { Limit = 200 });
            Assert.Equal(146, all.TotalCount); // 20 proficiencies + 4 budgets + 6 modern groups + 116 legacy areas.
            Assert.Equal(20, all.Rows.Count(row => Cell(row, "kind") == "Proficiency"));
            Assert.DoesNotContain(all.Rows, row => Cell(row, "kind") is "Modern perk" or "Legacy perk");
            Assert.Empty(GarageReader.Read(container, tree));
            var (_, _, context) = ReferencePackFixture.Embedded();
            var cyberware = new CyberwareInspection(InventoryReader.Read(container, tree), EquipmentData.Read(reader, container, tree,
                InventoryReader.Read(container, tree)), new CyberpunkReferences(context, 2210), container, tree);
            Assert.Equal(232, cyberware.Table.Metadata.TotalCount);
            Assert.All(cyberware.Table.Query(new QueryableCollectionQuery { Limit = 200 }).Rows, row =>
                Assert.Equal("Catalog only; not observed in classified inventory", Cell(row, "ownership")));
            var world = new WorldInspection(reader, container, tree, new CyberpunkReferences(context, 2210), null);
            Assert.Null(world.GarageWarning);
            Assert.Null(world.TravelWarning);
            Assert.Equal(105, world.Vehicles.Metadata.TotalCount);
            Assert.Equal(232, world.Travel.Metadata.TotalCount);
            var registered = world.Travel.Query(new QueryableCollectionQuery { Filters = new Dictionary<string, string>
                { ["state"] = "Registered in saved FastTravelSystem" } });
            var point = Assert.Single(registered.Rows);
            Assert.Equal("0x0000002486F51E9C", Cell(point, "id"));
            Assert.Contains("#hey_gle_dataterm_04", Cell(point, "details"));
            var stats = new ItemStatsInspection(container, tree, InventoryReader.Read(container, tree));
            Assert.Null(stats.Warning);
            var modifiers = stats.Table.Query(new QueryableCollectionQuery { Limit = 200 });
            output.WriteLine($"Equipment-independent item-stat rows: {modifiers.TotalCount}");
            Assert.Contains(modifiers.Rows, row => Cell(row, "details").Contains("Unique saved ItemID", StringComparison.Ordinal));
            Assert.DoesNotContain(modifiers.Rows, row => Cell(row, "class") == "unparsed");
            Assert.Contains(modifiers.Rows, row => Cell(row, "part").StartsWith("root/", StringComparison.Ordinal) &&
                Cell(row, "details") == "No saved stats entry");
            var journal = new JournalInspection(container, tree);
            Assert.Equal(594, journal.Table.Metadata.TotalCount);
            Assert.Contains("retained suffix 16 bytes", journal.Summary);
        }
    }

    [Fact]
    public void AllReadOnlyCollectionsStayBoundedAndSerializeWithoutChangingTheSave()
    {
        var file = ProgressionSampleTests.Files()[0];
        var (plugin, _, context) = ReferencePackFixture.Embedded();
        using var document = plugin.Open(file, context);
        var view = document.BuildView();
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        foreach (var collection in query.Collections)
        {
            var first = query.Query(collection.Id, new QueryableCollectionQuery { Limit = int.MaxValue });
            Assert.InRange(first.Rows.Count, 0, 200);
            Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(query.Query(collection.Id, new QueryableCollectionQuery { Limit = int.MaxValue })));
            Assert.Empty(query.Query(collection.Id, new QueryableCollectionQuery { Offset = int.MaxValue }).Rows);
        }
        Assert.True(JsonSerializer.Serialize<ViewNode>(view).Length < 100_000);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void MistypedEquipmentAndTravelHandlesAreRefusedIndependently()
    {
        var file = ProgressionSampleTests.Files()[0];
        var container = CsavContainer.Parse(file.ReadAllBytes());
        var tree = CsavTree.Parse(container);
        var reader = new ScriptableReader(container, tree);
        var system = reader.System("EquipmentSystem");
        var field = system.Require("ownerData", "array:handle:EquipmentSystemPlayerData");
        var node = Assert.Single(tree.Nodes, node => node.Name == "ScriptableSystemsContainer");
        var changed = container.Payload.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(changed.AsSpan(node.Offset + 8 + field.Offset + 4), int.MaxValue);
        var modified = CsavContainer.Parse(container.Rebuild(changed));
        var broken = new ScriptableReader(modified, tree);
        Assert.Throws<SaveFormatException>(() => EquipmentData.Read(broken, modified, tree, InventoryReader.Read(modified, tree)));
        Assert.Null(new DevelopmentInspection(broken, null).Warning);
        var world = new WorldInspection(broken, modified, tree, new CyberpunkReferences(IReferenceContext.Empty, 2210), null);
        Assert.Null(world.TravelWarning);
    }

    internal static string Cell(TableRow row, string key) => Assert.IsType<SectionNode>(row.Cells[key]).Title;
}
