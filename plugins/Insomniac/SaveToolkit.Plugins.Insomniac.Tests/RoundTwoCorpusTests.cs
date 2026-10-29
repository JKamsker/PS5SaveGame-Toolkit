using System.Numerics;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class RoundTwoCorpusTests
{
    [Fact]
    public void EveryGenerationCorroboratesArmourAndSpybotOrdinalJoins()
    {
        var files = SampleSaves.Load("PPSA01474").AllDiskFiles.Where(f => f.FileName == "i29-s.save" && !f.Directory.IsBackup).ToArray();
        Assert.Equal(36, files.Length);
        // RynoPlanSaveId ordinals -> measured GameLevelIndex array positions.
        int[] spybotLocations = [2, 3, 4, 4, 8, 7, 6, 5, 9, 10];
        const ulong BaseArmourMask = (1UL << 27) - 1;
        const ulong PreorderMask = 7UL << 9;
        var decodedWeapons = 0;
        var decodedVanity = 0;
        foreach (var file in files)
        {
            var container = Dat1Container.Parse(file.ReadAllBytes());
            var campaign = new DdlSections(container).Roots[RiftPlayerView.CampaignTag];
            var armour = campaign["VanityPickupsCollected"].Values.Single().AsUInt64();
            Assert.Equal(campaign["LevelVanityPickups"].Values.Sum(v => v.AsInt64()),
                BitOperations.PopCount(armour & BaseArmourMask & ~PreorderMask));
            var spybots = campaign["RYNOPlansCollected"].Values.Single().AsUInt64();
            var expected = new long[11];
            for (var bit = 0; bit < spybotLocations.Length; bit++)
                if ((spybots & (1UL << bit)) != 0) expected[spybotLocations[bit]]++;
            Assert.Equal(expected, campaign["LevelRYNOPlans"].Values.Select(v => v.AsInt64()));
            foreach (var record in InventoryReader.Read(container).SelectMany(b => b.Records))
            {
                Assert.NotNull(record.ItemId);
                if (record.Size == 119)
                {
                    Assert.NotNull(record.Upgrades);
                    Assert.InRange(record.Upgrades.SelectedCell, 0, 255);
                    decodedWeapons++;
                }
                if (record.Size == 460)
                {
                    Assert.NotNull(record.Vanity);
                    Assert.Equal(0u, record.Vanity.ExpirationTime);
                    decodedVanity++;
                }
            }
        }
        Assert.True(decodedWeapons > 500);
        Assert.True(decodedVanity > 1000);
    }

    [Fact]
    public void EveryCatalogWeaponPathMatchesAnObservedPs5ConfigToken()
    {
        var plugin = new InsomniacPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474");
        var configs = SampleSaves.Load("PPSA01474").AllDiskFiles
            .Where(f => f.FileName == "i29-s.save" && !f.Directory.IsBackup)
            .SelectMany(f => InventoryReader.Read(Dat1Container.Parse(f.ReadAllBytes())))
            .SelectMany(b => b.Records).Select(r => r.ConfigBytes).ToHashSet(StringComparer.Ordinal);
        foreach (var record in context.Catalog("rift-weapons")!.Search(new() { Limit = 200 }).Items)
            Assert.Contains(RiftAssetId.Hash(record.Key), configs);
    }

    [Fact]
    public void NewViewsArePagedReadOnlyAttributedAndKeepAllBytes()
    {
        var plugin = new InsomniacPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474");
        var file = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        using var document = plugin.Open(file, context);
        var view = Assert.IsType<TabsNode>(document.BuildView());
        foreach (var title in new[] { "Weapons", "Armour", "Upgrades", "Collectibles" })
            Assert.NotEmpty(Assert.Single(view.Tabs, t => t.Title == title).Children.OfType<QueryableTableNode>());
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var boundIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var collection in query.Collections)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var offset = 0; offset < collection.TotalCount; offset += 37)
            {
                var page = query.Query(collection.Id, new() { Offset = offset, Limit = 37 });
                Assert.InRange(page.Rows.Count, 1, 37);
                Assert.Equal(collection.TotalCount, page.FilteredCount);
                foreach (var row in page.Rows)
                {
                    Assert.True(keys.Add(row.Key));
                    foreach (var field in row.Cells.Values.OfType<FieldNode>())
                    {
                        Assert.True(field.Spec.ReadOnly);
                        Assert.Equal(field.Value, document.GetValue(field.ValueId));
                        Assert.False(string.IsNullOrWhiteSpace(field.Description));
                        boundIds.Add(field.ValueId);
                    }
                }
            }
            Assert.Equal(collection.TotalCount, keys.Count);
        }
        Assert.True(boundIds.Count > 1000);
        var first = query.Query("arsenal/weapons", new() { Text = "Burst Pistol" });
        Assert.NotEmpty(first.Rows);
        var xp = Assert.IsType<FieldNode>(first.Rows[0].Cells["xp"]);
        Assert.False(document.ApplyEdits([new(xp.ValueId, FieldValue.Of(0L))]).Succeeded);
        Assert.Throws<KeyNotFoundException>(() => document.InvokeAction("max-upgrades", new Dictionary<string, FieldValue>()));
        Assert.True(document.CanEdit);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.NotEmpty(query.Query("world/objectives", new() { Text = "MIS_ZURK" }).Rows);
    }

    [Fact]
    public void MissingPacksPreserveConfigTokensAndHighUnknownCollectionBits()
    {
        using var document = new InsomniacPlugin().Open(PlayerCorpusTests.Find("i29slot0-s-autosave", 0));
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var weapons = query.Query("arsenal/weapons", new() { Text = "b33fb3994f5e7ec3" });
        Assert.NotEmpty(weapons.Rows);
        Assert.All(weapons.Rows, row => Assert.Equal(Confidence.Unknown, Assert.IsType<FieldNode>(row.Cells["name"]).Confidence));
        var armour = query.Query("armour/collection", new() { Offset = 57, Limit = 7 });
        Assert.Equal(7, armour.Rows.Count);
        Assert.All(armour.Rows, row => Assert.Equal(Confidence.Unknown, Assert.IsType<FieldNode>(row.Cells["name"]).Confidence));
    }

    [Fact]
    public void CoreAcceptsEveryCollectionPageAndKeepsUnprovenWeaponStateReadOnly()
    {
        var plugin = new InsomniacPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474");
        var file = Assert.IsType<DiskSaveFile>(PlayerCorpusTests.Find("i29slot0-s-autosave", 0));
        var document = plugin.Open(file, context);
        using var session = new DocumentSession("insomniac-r2", file, plugin, document, references: context);
        _ = session.BuildView();
        foreach (var collection in session.QueryableCollections)
        {
            var page = session.QueryCollection(collection.Id, new() { Limit = 19 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(0, page.Revision);
            Assert.InRange(page.Rows.Count, 0, 19);
        }
        var burst = Assert.Single(session.QueryCollection("arsenal/weapons", new()
        {
            Text = "Burst Pistol", Filters = new Dictionary<string, string> { ["container"] = "62d731c8/0" },
        }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows);
        Assert.Equal(203847, Assert.IsType<FieldNode>(burst.Cells["xp"]).Value.AsInt64());
        Assert.Equal(190, Assert.IsType<FieldNode>(burst.Cells["ammo"]).Value.AsDouble());
        var bears = Assert.Single(session.QueryCollection("collectibles/awards", new() { Text = "All_CraigBears" }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows);
        Assert.Equal(1, Assert.IsType<FieldNode>(bears.Cells["value"]).Value.AsInt64());
        Assert.False(Assert.IsType<FieldNode>(bears.Cells["prize"]).Value.AsBool());
        Assert.True(document.CanEdit);
    }
}
