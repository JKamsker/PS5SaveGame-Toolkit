using System.Globalization;
using SaveToolkit.Codecs.Sqlite;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsDiscoverySampleTests
{
    // Aggregate observations only; no private rows, identifiers or save bytes are embedded.
    // Image order: false = RawDatabaseImage, true = RawExclusiveImage. Slots are not chronology.
    [Theory]
    [InlineData("HL0000", false, "9", 15, 1542, 47, 11, 206, 60, 43, 43)]
    [InlineData("HL0000", true,  "8", 15, 1542, 47, 11, 206, 60, 43, 43)]
    [InlineData("HL0001", false, "9", 15, 1555, 49, 11, 212, 60, 44, 44)]
    [InlineData("HL0001", true,  "9", 15, 1545, 49, 11, 208, 60, 44, 44)]
    [InlineData("HL0002", false, "8", 17, 1748, 49, 11, 226, 65, 49, 49)]
    [InlineData("HL0002", true,  "8", 17, 1736, 49, 11, 226, 65, 48, 48)]
    [InlineData("HL0003", false, "9", 17, 1776, 49, 11, 226, 66, 50, 50)]
    [InlineData("HL0003", true,  "9", 17, 1776, 49, 11, 226, 66, 50, 50)]
    [InlineData("HL0004", false, "4", 23, 1850, 49, 11, 248, 69, 52, 53)]
    [InlineData("HL0004", true,  "4", 23, 1850, 49, 11, 248, 69, 52, 53)]
    [InlineData("HL0005", false, "4", 23, 1907, 49, 11, 299, 70, 53, 1)]
    [InlineData("HL0005", true,  "4", 23, 1850, 49, 11, 248, 69, 52, 53)]
    [InlineData("HL0006", false, "5", 23, 2009, 51, 13, 314, 71, 59, 5)]
    [InlineData("HL0006", true,  "4", 23, 1850, 49, 11, 248, 69, 52, 53)]
    [InlineData("HL0007", false, "5", 25, 2082, 56, 20, 331, 76, 63, 9)]
    [InlineData("HL0007", true,  "4", 23, 1850, 49, 11, 248, 69, 52, 53)]
    [InlineData("HL0008", false, "7", 15, 1330, 46, 11, 183, 60, 41, 41)]
    [InlineData("HL0008", true,  "6", 15, 1274, 45, 11, 176, 58, 40, 40)]
    [InlineData("HL0009", false, "8", 15, 1530, 46, 11, 199, 60, 42, 42)]
    [InlineData("HL0009", true,  "8", 15, 1471, 46, 11, 197, 60, 42, 42)]
    [InlineData("HL0010", false, "4", 25, 2082, 56, 20, 331, 76, 61, 7)]
    [InlineData("HL0010", true,  "4", 23, 1850, 49, 11, 248, 69, 52, 53)]
    [InlineData("HL0011", false, "5", 25, 2082, 56, 20, 331, 76, 63, 9)]
    [InlineData("HL0011", true,  "4", 23, 1850, 49, 11, 248, 69, 52, 53)]
    [InlineData("HL0012", false, "5", 25, 2082, 56, 20, 331, 76, 63, 9)]
    [InlineData("HL0012", true,  "4", 23, 1850, 49, 11, 248, 69, 52, 53)]
    [InlineData("HL0013", false, "4", 25, 2082, 56, 20, 331, 75, 60, 6)]
    [InlineData("HL0013", true,  "4", 23, 1850, 49, 11, 248, 69, 52, 53)]
    [InlineData("HL0014", false, "4", 25, 2082, 56, 20, 331, 75, 60, 6)]
    [InlineData("HL0014", true,  "4", 23, 1850, 49, 11, 248, 69, 52, 53)]
    public void EveryImage_PinsPointsTalentsCollectionAndDependencyEvidence(string slot, bool exclusive,
        string points, int perks, int collections, int lore, int pages, int conflictingTuples,
        int challenges, int complete, int delayed)
    {
        var samples = SampleSaves.Load("PPSA01603").AllDiskFiles.Where(file =>
                file.FileName == "ue4savegame.ps5.sav" && file.Directory.Name.StartsWith("HL00", StringComparison.Ordinal))
            .DistinctBy(file => file.Sha256).ToArray();
        Assert.Equal(15, samples.Length);
        var sample = Assert.Single(samples, file => file.Directory.Name == slot);
        byte[] original = sample.ReadAllBytes();
        var gvas = GvasFile.Parse(original);
        string imageName = exclusive ? "RawExclusiveImage" : "RawDatabaseImage";
        byte[] bytes = HogwartsImage.ReadArray(gvas, gvas.Properties.Single(node => node.Name == imageName));
        using var image = SqliteImage.Open(bytes);
        var pointRow = Assert.Single(image.Query("SELECT DataValue,typeof(DataValue) FROM MiscDataDynamic WHERE DataOwner='Player0' AND DataName='PerkPoints'").Rows);
        Assert.Equal(points, pointRow[0]);
        Assert.Equal("text", pointRow[1]);
        Assert.Equal(1, Count(image, "SELECT count(*) FROM MiscDataDynamic WHERE DataName='PerkPoints'"));
        Assert.Equal(perks, Count(image, "SELECT count(*) FROM PerkDynamic"));
        Assert.Equal(perks, Count(image, "SELECT count(DISTINCT PerkID) FROM PerkDynamic WHERE Level=1 AND typeof(Level)='integer'"));
        Assert.Equal(collections, Count(image, "SELECT count(*) FROM CollectionDynamic"));
        Assert.Equal(lore, Count(image, "SELECT count(DISTINCT ItemID) FROM CollectionDynamic WHERE ItemID GLOB 'LORE_*' AND ItemState='Obtained'"));
        Assert.Equal(150, Count(image, "SELECT count(*) FROM CollectionDynamic WHERE ItemID GLOB 'LORE_*' AND ItemState='Unknown'"));
        Assert.Equal(lore, Count(image, "SELECT count(DISTINCT c.ItemID) FROM CollectionDynamic c JOIN CollectionDynamic u ON c.CategoryID=u.CategoryID AND c.SubcategoryID=u.SubcategoryID AND c.ItemID=u.ItemID WHERE c.ItemID GLOB 'LORE_*' AND c.ItemState='Obtained' AND u.ItemState='Unknown'"));
        Assert.Equal(pages, Count(image, "SELECT count(*) FROM MiscDataDynamic WHERE DataName GLOB 'PageCollected*' AND DataValue='1' AND typeof(DataValue)='text'"));
        Assert.Equal(pages, Count(image, "SELECT count(*) FROM MiscDataDynamic WHERE DataName GLOB 'PageCollected*'"));
        Assert.Equal(conflictingTuples, Count(image, "SELECT count(*) FROM (SELECT 1 FROM CollectionDynamic GROUP BY CategoryID,SubcategoryID,ItemID HAVING count(DISTINCT ItemState)>1)"));
        Assert.Equal(challenges, Count(image, "SELECT count(*) FROM AchievementDynamic WHERE AchievementID GLOB 'FGC_*'"));
        Assert.Equal(complete, Count(image, "SELECT count(*) FROM AchievementDynamic WHERE AchievementID GLOB 'FGC_*' AND IsComplete<>0"));
        Assert.Equal(delayed, Count(image, "SELECT count(*) FROM AchievementDynamic WHERE AchievementID GLOB 'FGC_*' AND RewardsDelayed<>0"));
        AssertMissingTalentJoins(image);
        image.ValidateIntegrity();
        Assert.Equal(bytes, image.Serialize());
        using var independent = SqliteImage.Open(HogwartsImage.Decode(HogwartsImage.EncodeStored(bytes)));
        independent.ValidateIntegrity();
        Assert.Equal(bytes, independent.Serialize());
        Assert.Equal(original, gvas.Serialize());

        using var document = new UnrealGvasPlugin().Open(sample);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        string prefix = $"hogwarts/{imageName}/";
        var point = Assert.IsType<FieldNode>(Assert.Single(query.Query(prefix + "MiscDataDynamic", new()
            { Filters = new Dictionary<string, string> { ["DataOwner"] = "Player0", ["DataName"] = "PerkPoints" } }).Rows).Cells["DataValue"]);
        Assert.Equal(points, point.Value.AsString());
        Assert.True(point.Spec.ReadOnly);
        Assert.Equal(Confidence.Inferred, point.Confidence);
        Assert.False(document.ApplyEdits([new(point.ValueId, point.Value)]).Succeeded);
        foreach (string table in new[] { "PerkDynamic", "CollectionDynamic" })
            Assert.All(query.Query(prefix + table, new()).Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>(),
                field => Assert.True(field.Spec.ReadOnly));
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static void AssertMissingTalentJoins(SqliteImage image)
    {
        Assert.Equal(0, Count(image, "SELECT count(*) FROM CharactersDynamic"));
        Assert.Equal(0, Count(image, "SELECT count(*) FROM BuffsDynamic"));
        Assert.Equal(0, Count(image, "SELECT count(*) FROM BuffSlotsDynamic"));
        Assert.Equal(2, Count(image, "SELECT count(*) FROM RPGPermaAbilityDynamic"));
        Assert.Equal(0, Count(image, "SELECT count(*) FROM RPGPermaAbilityDynamic a JOIN PerkDynamic p ON a.AbilityName=p.PerkID"));
        Assert.Equal(0, Count(image, "SELECT count(*) FROM PerkDynamic p JOIN LocksDynamic l ON p.PerkID=l.LockID"));
        Assert.Equal(0, Count(image, "SELECT count(*) FROM sqlite_schema WHERE type='table' AND instr(upper(sql),'REFERENCES')>0"));
        Assert.Equal("PerkID,Level", string.Join(',', image.Query("SELECT * FROM PerkDynamic LIMIT 0").Columns));
        Assert.Equal("CategoryID,SubcategoryID,ItemID,ItemState,UpdateTime", string.Join(',', image.Query("SELECT * FROM CollectionDynamic LIMIT 0").Columns));
    }

    private static long Count(SqliteImage image, string sql) =>
        Convert.ToInt64(Assert.Single(image.Query(sql).Rows)[0], CultureInfo.InvariantCulture);

    [Fact]
    public void Hl0010_LockAndLootJoinsDoNotResolveConflictingCollectionStates()
    {
        var sample = SampleSaves.Load("PPSA01603").AllDiskFiles.First(file =>
            file.Directory.Name == "HL0010" && file.FileName == "ue4savegame.ps5.sav");
        var gvas = GvasFile.Parse(sample.ReadAllBytes());
        using var image = SqliteImage.Open(HogwartsImage.ReadArray(gvas,
            gvas.Properties.Single(node => node.Name == "RawDatabaseImage")));
        foreach (string state in new[] { "Obtained", "Unknown" })
        {
            var parameters = new Dictionary<string, object?> { ["$state"] = state };
            Assert.Equal(14L, Assert.Single(image.Query("""
                SELECT count(DISTINCT c.ItemID) FROM CollectionDynamic c
                JOIN LocksDynamic l ON l.LockID=c.ItemID
                WHERE c.CategoryID='WandStyle' AND c.ItemState=$state AND l.ELockState=0
                """, parameters).Rows)[0]);
            Assert.Equal(34L, Assert.Single(image.Query("""
                SELECT count(DISTINCT c.ItemID) FROM CollectionDynamic c
                JOIN LootItemsDynamic l ON l.ItemID=c.ItemID OR l.ItemID='Recipe_Transfiguration_'||c.ItemID
                WHERE c.CategoryID='Conjurations' AND c.ItemState=$state
                """, parameters).Rows)[0]);
        }
    }
}
