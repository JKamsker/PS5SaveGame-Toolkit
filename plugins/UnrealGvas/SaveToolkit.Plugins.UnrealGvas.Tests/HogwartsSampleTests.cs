using SaveToolkit.Codecs.Sqlite;
using SaveToolkit.Core.References;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsSampleTests
{
    private static readonly string[] InventoryIdentity = ["CharacterID", "HolderID", "SlotNumber"];
    [Fact]
    public void EveryDistinctGameplayImage_DecodesWithIndependentIntegrityAndExactNoOp()
    {
        var files = SampleSaves.Load("PPSA01603").AllDiskFiles
            .Where(file => file.FileName == "ue4savegame.ps5.sav" && file.Directory.Name.StartsWith("HL00", StringComparison.Ordinal))
            .DistinctBy(file => file.Sha256).ToArray();
        Assert.Equal(15, files.Length);
        long inventorySlots = 0, emptySlots = 0, namedZeroCounts = 0;
        foreach (var sample in files)
        {
            var file = GvasFile.Parse(sample.ReadAllBytes());
            foreach (string name in new[] { "RawDatabaseImage", "RawExclusiveImage" })
            {
                byte[] image = HogwartsImage.ReadArray(file, Assert.Single(file.Properties, node => node.Name == name));
                using var database = new HogwartsDatabase(name, image);
                Assert.Equal(84, database.Tables.Count);
                Assert.Equal(image, database.SerializeForResearch());
                using var independent = SqliteImage.Open(database.SerializeForResearch());
                Assert.Equal(database.Tables.Select(table => table.Name), independent.ListTables());
                Assert.Contains(database.Tables, table => table.Name == "InventoryDynamic" &&
                    table.IdentityColumns.SequenceEqual(InventoryIdentity));
                var inventory = database.Tables.Single(table => table.Name == "InventoryDynamic");
                int itemIndex = inventory.Columns.ToList().IndexOf("ItemID") + 1;
                int variationIndex = inventory.Columns.ToList().IndexOf("Variation") + 1;
                int countIndex = inventory.Columns.ToList().IndexOf("Count") + 1;
                long imageEmptySlots = 0;
                for (int offset = 0; offset < inventory.Count; offset += QueryableCollectionLimits.MaxPageSize)
                {
                    var page = database.Query(inventory, new() { Offset = offset, Limit = QueryableCollectionLimits.MaxPageSize, IncludeEmpty = true });
                    Assert.Equal(inventory.Count, page.Filtered);
                    Assert.Equal(0, page.HiddenEmpty);
                    foreach (var row in page.Result.Rows)
                    {
                        inventorySlots++;
                        if (row[itemIndex] is null)
                        {
                            Assert.Null(row[variationIndex]);
                            Assert.Equal(0L, row[countIndex]);
                            imageEmptySlots++;
                        }
                        else if (row[countIndex] is 0L) namedZeroCounts++;
                    }
                }
                var hidden = database.Query(inventory, new());
                Assert.Equal(imageEmptySlots, hidden.HiddenEmpty);
                Assert.Equal(inventory.Count - imageEmptySlots, hidden.Filtered);
                emptySlots += imageEmptySlots;
            }
        }
        Assert.Equal(44_572, inventorySlots);
        Assert.Equal(21_387, emptySlots);
        Assert.Equal(30, namedZeroCounts);
    }

    [Fact]
    public void Hl0010_HasNamedTabsIndependentQueryableImagesAndNoEditableMetadata()
    {
        var sample = SampleSaves.Load("PPSA01603").AllDiskFiles.First(file => file.Directory.Name == "HL0010");
        var plugin = new UnrealGvasPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01603");
        using var document = plugin.Open(sample, context);
        Assert.True(document.CanEdit);
        var tabs = Assert.IsType<TabsNode>(document.BuildView()).Tabs;
        foreach (string name in new[] { "Character", "Inventory", "Progression", "Collections" })
            Assert.Contains(tabs, tab => tab.Title == name && tab.Children.OfType<QueryableTableNode>().Any());
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.Equal(168, queryable.Collections.Count);
        var inventory = Assert.Single(queryable.Collections, collection => collection.Id == "hogwarts/RawDatabaseImage/InventoryDynamic");
        var page = queryable.Query(inventory.Id, new() { Filters = new Dictionary<string, string> { ["ItemID"] = "Knuts" } });
        Assert.NotEmpty(page.Rows);
        Assert.All(page.Rows, row => Assert.Equal("Knuts", row.Cells["ItemID"] switch
        {
            TextNode text => text.Text,
            FieldNode field => field.Value.AsString(),
            _ => throw new Xunit.Sdk.XunitException("Unexpected ItemID cell type."),
        }));
        Assert.True(page.FilteredCount < inventory.TotalCount);
        Assert.False(document.ApplyEdits([new("property/0", SaveToolkit.Sdk.Values.FieldValue.Of("PC"))]).Succeeded);
        Assert.True(sample.ReadAllBytes().AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Fact]
    public void Catalog_IsPinnedLicensedAndJoinsActualPs5IdsWithReadOnlyPickers()
    {
        var plugin = new UnrealGvasPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var context = store.Pin(plugin, "PPSA01603");
        var catalog = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog("unrealgvas_hogwarts_collectibles"));
        Assert.Equal(699, catalog.Count);
        Assert.Equal("MIT", catalog.Dataset.Attribution.License);
        Assert.Contains("02525dde0fd9bc0be72982478680377dccb41246", catalog.Dataset.Attribution.SourceUrl);
        Assert.Null(store.Pin(plugin, "PPSA01288").Catalog(catalog.Id));
        var sample = SampleSaves.Load("PPSA01603").AllDiskFiles.First(file => file.Directory.Name == "HL0010");
        using var document = plugin.Open(sample, context);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        int matches = 0;
        foreach (var collection in queryable.Collections.Where(collection => collection.Id.StartsWith("hogwarts/RawDatabaseImage/", StringComparison.Ordinal)))
        {
            for (int offset = 0; offset < collection.TotalCount; offset += QueryableCollectionLimits.MaxPageSize)
            {
                var page = queryable.Query(collection.Id, new() { Offset = offset, Limit = QueryableCollectionLimits.MaxPageSize });
                foreach (var field in page.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>())
                {
                    matches++;
                    if (field.Spec is SaveToolkit.Sdk.Values.ReferenceSpec reference)
                    {
                        Assert.True(reference.ReadOnly);
                        Assert.Contains("Malin001", field.Description);
                    }
                    Assert.Equal(field.Value, document.GetValue(field.ValueId));
                }
            }
        }
        Assert.True(matches > 100, $"Expected substantial exact catalog joins; found {matches}.");
    }

    [Fact]
    public void DamagedPrimaryImage_DisablesOnlyThatOverlayAndPreservesEveryOriginalByte()
    {
        var sample = SampleSaves.Load("PPSA01603").AllDiskFiles.First(file =>
            file.Directory.Name == "HL0010" && file.FileName == "ue4savegame.ps5.sav");
        byte[] damaged = sample.ReadAllBytes();
        var gvas = GvasFile.Parse(damaged);
        damaged[Assert.Single(gvas.Properties, node => node.Name == "RawDatabaseImage").Offset + sizeof(int)] ^= 1;
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("save", damaged, "PPSA01603"));
        Assert.False(document.CanEdit);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.Equal(84, queryable.Collections.Count);
        Assert.All(queryable.Collections, collection => Assert.StartsWith("hogwarts/RawExclusiveImage/", collection.Id));
        var summary = Assert.IsType<TabsNode>(document.BuildView()).Tabs[0];
        Assert.Contains(summary.Children.OfType<TextNode>(), node => node.Tone == Tone.Warning && node.Text.Contains("validation failed", StringComparison.Ordinal));
        Assert.Equal(damaged, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void HogwartsMetadataFiles_AlsoRemainReadOnly()
    {
        foreach (var sample in SampleSaves.Load("PPSA01603").AllDiskFiles.Where(file =>
            file.FileName == "ue4savegame.ps5.sav" && !file.Directory.Name.StartsWith("HL00", StringComparison.Ordinal)))
        {
            using var document = new UnrealGvasPlugin().Open(sample);
            Assert.False(document.CanEdit);
            Assert.All(GvasFormatTests.Expand(document), field => Assert.True(field.Spec.ReadOnly));
        }
    }

    [Fact]
    public void UnverifiedHogwartsHeader_FallsBackToReadOnlyEvenWithoutTitleMetadata()
    {
        var sample = SampleSaves.Load("PPSA01603").AllDiskFiles.First(file =>
            file.Directory.Name == "HL0010" && file.FileName == "ue4savegame.ps5.sav");
        byte[] changed = sample.ReadAllBytes();
        // UE4 legacy header: magic, save/package versions, major/minor, then patch at byte 16.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(16), 3);
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("save", changed));
        Assert.False(document.CanEdit);
        Assert.False(document is IQueryableDocument);
        Assert.All(GvasFormatTests.Expand(document), field => Assert.True(field.Spec.ReadOnly));
        Assert.Equal(changed, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
