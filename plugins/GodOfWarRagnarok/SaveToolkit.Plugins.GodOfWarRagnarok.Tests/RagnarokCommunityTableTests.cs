using System.Buffers.Binary;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokCommunityTableTests
{
    private const string License = "No licence published; reused with attribution per owner decision 2026-10-28";
    private static readonly (ulong Id, int Count)[] Trees =
        [(0x1013865FC6, 28), (0x38EA4A0828266819, 26), (0x14BAA9043ACBB11, 27), (0x7A134D7DF345D66, 28)];

    [Fact]
    public void PacksContainPinnedAttributedFactsAndNoImportedSafetyClaims()
    {
        var context = Context();
        foreach (var (id, count) in new[] { (RagnarokCommunityTables.RuntimeCatalog, 961),
                     (RagnarokCommunityTables.TreeCatalog, 4), (RagnarokCommunityTables.OlderCatalog, 1682) })
        {
            var catalog = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog(id, "PS5 revision 0x00513040"));
            Assert.Equal(count, catalog.Count);
            Assert.Equal(License, catalog.Dataset.Attribution.License);
            Assert.Contains(id == RagnarokCommunityTables.OlderCatalog ? "9342d5f3967ef74bac1161581dc4eab8bb41760d" :
                "7b1730217b92d27ab910d6d421efbd503cc109b2", catalog.Dataset.Attribution.SourceUrl);
            for (var offset = 0; offset < count; offset += 200)
            foreach (var record in catalog.Search(new() { Offset = offset, Limit = 200 }).Items)
            {
                Assert.Matches("^[0-9A-F]{16}$", record.Key);
                Assert.Null(record.Limit);
                Assert.Null(record.AssetId);
                Assert.Contains(License, record.AttributionText);
                Assert.DoesNotContain("safe_edit", record.Attributes.Keys);
            }
        }
    }

    [Fact]
    public void DirectAndPinnedCallersResolveTheSameNamesAndPreserveRawUnknowns()
    {
        var context = Context();
        var direct = new RagnarokReferences(IReferenceContext.Empty);
        var pinned = new RagnarokReferences(context);
        var catalog = context.Catalog(RagnarokCommunityTables.RuntimeCatalog, "PS5 revision 0x00513040")!;
        for (var offset = 0; offset < catalog.Count; offset += 200)
        foreach (var record in catalog.Search(new() { Offset = offset, Limit = 200 }).Items)
        {
            var id = BinaryPrimitives.ReadUInt64LittleEndian(Convert.FromHexString(record.Key));
            Assert.Equal(pinned.Name(id), direct.Name(id));
            Assert.Equal(pinned.Find(id)!.Dataset.Id, direct.Find(id)!.Dataset.Id);
        }

        Assert.Equal("Kratos XP", direct.Name(0x9C7CF61C839B9A8A));
        Assert.Equal("XP", direct.Category(0xAC98E8B5155F2C73));
        Assert.Equal("Atreus XP", direct.Name(0xA1D028F9B467E25C));
        Assert.Equal("Freya XP", direct.Name(0xE577D004DA4F5243));
        Assert.Equal("Dwarven Steel", direct.Name(0x2D218DADA5D3E28F));
        Assert.Null(direct.Community.Runtime(RagnarokReferences.Key(Trees[0].Id)));
        Assert.Null(direct.Community.Tree(0x9C7CF61C839B9A8A));
        Assert.Equal("Unknown ID 0x0000000000000001", direct.Name(1));
        Assert.Null(direct.Catalog(1));
        var older = direct.Find(0x225DDF8664908400)!;
        Assert.StartsWith("2018 candidate: ", older.Name);
        Assert.Equal("Unknown", older.Attributes["confidence"]);
        Assert.Equal("KratosArmorLegs_TankSet_Tier01_Reinforce2", older.Attributes["internalName"]);
    }

    [Fact]
    public void EveryPrimaryGenerationConfirmsResourceAndGroupJoinsWithoutInventingGearOrQuestJoins()
    {
        var references = new RagnarokReferences(Context());
        var runtimeIds = new HashSet<ulong>();
        var olderIds = new HashSet<ulong>();
        var files = SampleSaves.Load("PPSA08330").AllDiskFiles.Where(file =>
            !file.Directory.IsBackup && file.FileName.StartsWith("GOWRSAVE", StringComparison.Ordinal)).ToArray();
        Assert.Equal(76, files.Length);
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            var index = RagnarokRecordIndex.Parse(bytes, false);
            foreach (var (id, count) in Trees)
            {
                var group = Assert.Single(index.Nested.FlagGroups, group => group.Id == id);
                Assert.Equal(count, group.Count);
                Assert.Contains("inferred", references.Community.Tree(id)!.Name);
            }
            Assert.Null(references.Community.Tree(0x11CBBEE78698EC9));
            foreach (var wallet in index.Nested.Wallets)
            {
                for (var row = 0; row < wallet.ResourceCount; row++)
                {
                    var id = RagnarokFormat.UInt64(bytes, wallet.ResourcesOffset + row * RagnarokFormat.WalletRowSize);
                    var key = RagnarokReferences.Key(id);
                    if (references.Community.Runtime(key) is not null) runtimeIds.Add(id);
                    if (references.Community.Older(key) is not null) olderIds.Add(id);
                }
                for (var row = 0; row < wallet.IdCount; row++)
                    AssertNoItemMapping(RagnarokFormat.UInt64(bytes, wallet.IdsOffset + row * sizeof(ulong)), references);
            }
            for (var row = 0; row < index.DirectoryCount; row++)
                AssertNoItemMapping(RagnarokFormat.UInt64(bytes, RagnarokFormat.DirectoryItemsOffset + row * sizeof(ulong)), references);
            foreach (var group in index.Nested.FlagGroups)
            for (var row = 0; row < group.Count; row++)
                AssertNoItemMapping(RagnarokFormat.UInt64(bytes, group.RowsOffset + row * RagnarokFlagGroup.RowSize), references);
        }
        Assert.Equal(211, runtimeIds.Count);
        Assert.Equal(78, runtimeIds.Count(id => references.Community.Runtime(RagnarokReferences.Key(id))!.Category != "Unknown"));
        Assert.Equal(16, olderIds.Count);
        Assert.Subset(runtimeIds, olderIds);
    }

    [Fact]
    public void CrossGameReferenceRemainsUnknownAndReadOnlyWhenAnExactIdIsPresent()
    {
        var file = SampleSaves.Load("PPSA08330").AllDiskFiles.First(file => file.FileName == "GOWRSAVE0");
        var bytes = file.ReadAllBytes();
        var index = RagnarokRecordIndex.Parse(bytes, false);
        // Synthetic identity fixture exercises the fallback, not evidence that this 2018 item exists on PS5.
        var offset = index.Nested.Wallets[0].IdsOffset;
        const ulong olderArmorId = 0x225DDF8664908400;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), olderArmorId);
        using var document = new GodOfWarRagnarokPlugin().Open(MemorySaveSet.SingleFile(file.FileName, bytes, "PPSA08330"), Context());
        var page = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("nested-ids", new() { Text = "2018 candidate: Reaver Belt Lv3" });
        var field = Assert.IsType<FieldNode>(Assert.Single(page.Rows).Cells["id"]);
        Assert.True(field.Spec.ReadOnly);
        Assert.Equal(Confidence.Unknown, field.Confidence);
        Assert.Contains("not a verified Ragnarök identity", field.Description);
        Assert.Contains(License, field.Description);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void RuntimeNamesBrowseOnlyIdsAndGroupAttributionReachBoundedViewsWithoutChangingBytes()
    {
        var file = SampleSaves.Load("PPSA08330").AllDiskFiles.First(file => file.FileName == "GOWRSAVE0");
        var plugin = new GodOfWarRagnarokPlugin();
        using var document = plugin.Open(file, Context());
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var steel = Assert.Single(queryable.Query("resources", new() { Text = "Dwarven Steel" }).Rows);
        var identity = Assert.IsType<FieldNode>(steel.Cells["id"]);
        Assert.True(identity.Spec.ReadOnly);
        Assert.Equal(Confidence.Inferred, identity.Confidence);
        Assert.Contains(License, identity.Description);
        Assert.Equal("8FE2D3A5AD8D212D", identity.Value.AsString());
        var xp = queryable.Query("xp", new() { Filters = new Dictionary<string, string> { ["container"] = "Hero wallet (inferred)" } });
        Assert.Equal(3, xp.Rows.Count);
        Assert.All(xp.Rows, row => Assert.Contains(License, Assert.IsType<TextNode>(row.Cells["source"]).Text));
        var flags = queryable.Query("flags", new() { Filters = new Dictionary<string, string> { ["group-name"] = "Leviathan Axe skill tree (inferred)" } });
        Assert.Equal(28, flags.FilteredCount);
        Assert.All(flags.Rows, row => Assert.Contains(License, Assert.IsType<TextNode>(row.Cells["group-source"]).Text));
        var unknowns = queryable.Query("resources", new() { Text = "Unknown runtime ID", Limit = 200 });
        Assert.NotEmpty(unknowns.Rows);
        Assert.All(unknowns.Rows, row => Assert.Equal(Confidence.Unknown, Assert.IsType<FieldNode>(row.Cells["id"]).Confidence));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static void AssertNoItemMapping(ulong id, RagnarokReferences references)
    {
        var key = RagnarokReferences.Key(id);
        Assert.Null(references.Community.Runtime(key));
        Assert.Null(references.Community.Older(key));
    }

    private static IReferenceContext Context()
    {
        var plugin = new GodOfWarRagnarokPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        return store.Pin(plugin, "PPSA08330");
    }
}
