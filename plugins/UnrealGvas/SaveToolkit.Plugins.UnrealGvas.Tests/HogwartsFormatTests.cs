using System.Buffers.Binary;
using SaveToolkit.Codecs.Sqlite;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsFormatTests
{
    [Fact]
    public void StoredFrames_CrossChunkBoundariesAndReopenAsAnIndependentSqliteImage()
    {
        using var database = SqliteImage.Create();
        database.Execute("CREATE TABLE Payload(Id INTEGER PRIMARY KEY, Bytes BLOB); INSERT INTO Payload VALUES(1, zeroblob(300000));");
        byte[] image = database.Serialize();
        byte[] frames = HogwartsImage.EncodeStored(image);
        Assert.True(image.Length > 2 * HogwartsImage.ChunkBytes);
        Assert.Equal(image, HogwartsImage.Decode(frames));
        using var reopened = SqliteImage.Open(HogwartsImage.Decode(frames));
        Assert.Equal(300000L, reopened.Query("SELECT length(Bytes) FROM Payload").Rows[0][0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(40)]
    public void CorruptFrameDeclarations_AreRejectedBeforeAllocation(int fieldOffset)
    {
        byte[] frames = HogwartsImage.EncodeStored(Image());
        BinaryPrimitives.WriteUInt64LittleEndian(frames.AsSpan(fieldOffset), ulong.MaxValue);
        Assert.Throws<SaveFormatException>(() => HogwartsImage.Decode(frames));
    }

    [Fact]
    public void PrefixMagicCountsAndTruncatedOrTrailingFrames_AreRejected()
    {
        byte[] frames = HogwartsImage.EncodeStored(Image());
        Assert.Throws<SaveFormatException>(() => HogwartsImage.Decode(frames.AsSpan(0, frames.Length - 1)));
        Assert.Throws<SaveFormatException>(() => HogwartsImage.Decode([.. frames, 0]));
        Assert.Throws<SaveFormatException>(() => HogwartsImage.Decode([]));
        // Stored Kraken has a two-byte CC 06 header; decoded prefix follows immediately.
        foreach (int position in new[] { 0, sizeof(int), HogwartsImage.PrefixBytes })
        {
            byte[] corrupt = frames.ToArray();
            corrupt[HogwartsImage.FrameBytes + 2 + position] ^= 0x40;
            Assert.Throws<SaveFormatException>(() => HogwartsImage.Decode(corrupt));
        }
        byte[] save = GvasFixture.Build(writer => GvasFixture.Property(writer, "RawDatabaseImage", "ArrayProperty",
            GvasFixture.Body(body => { body.Write(frames.Length + 1); body.Write(frames); }), "ByteProperty"));
        Assert.Throws<SaveFormatException>(() => GvasFile.Parse(save));
    }

    [Fact]
    public void Query_PagesCompleteFilteredResultsWithStableKeysAndRejectsInjectionAndUnknownColumns()
    {
        using var source = SqliteImage.Create();
        source.Execute("""
            CREATE TABLE InventoryDynamic(CharacterID TEXT, HolderID TEXT, SlotNumber INTEGER, ItemID TEXT, Count INTEGER);
            WITH RECURSIVE numbers(n) AS (VALUES(0) UNION ALL SELECT n+1 FROM numbers WHERE n<204)
            INSERT INTO InventoryDynamic SELECT 'Player0','ActorBackpack',n,CASE WHEN n%2=0 THEN 'Knuts' ELSE 'Potion' END,n FROM numbers;
            """);
        using var database = new HogwartsDatabase("synthetic", source.Serialize());
        var table = Assert.Single(database.Tables);
        var query = new SaveToolkit.Sdk.Collections.QueryableCollectionQuery
        {
            Text = "knUTs", SortColumn = "Count", Descending = true, Limit = 50,
        };
        var first = database.Query(table, query);
        var second = database.Query(table, query with { Offset = 50 });
        Assert.Equal(103, first.Filtered);
        Assert.Equal(50, first.Result.Rows.Count);
        Assert.Equal(204L, first.Result.Rows[0][5]);
        Assert.Empty(first.Result.Rows.Select(table.RowKey).Intersect(second.Result.Rows.Select(table.RowKey)));
        var byCount = database.Query(table, new() { Filters = new Dictionary<string, string> { ["Count"] = "204" } });
        Assert.Equal(table.RowKey(first.Result.Rows[0]), table.RowKey(Assert.Single(byCount.Result.Rows)));
        Assert.Empty(database.Query(table, query with { Text = "' OR 1=1 --" }).Result.Rows);
        Assert.Throws<ArgumentException>(() => database.Query(table, query with { SortColumn = "Count; DROP TABLE InventoryDynamic" }));
        Assert.Throws<ArgumentException>(() => database.Query(table, query with { Filters = new Dictionary<string, string> { ["missing"] = "1" } }));
        Assert.Throws<ArgumentException>(() => database.Query(table, query with { Limit = 201 }));
        Assert.Throws<ArgumentException>(() => database.Query(table, query with { Offset = -1 }));
        Assert.Throws<ArgumentException>(() => database.Query(table, query with { Cursor = "invalid" }));
        Assert.Equal(205, Assert.Single(database.Tables).Count);
    }

    [Fact]
    public void DuplicateLogicalKeys_FallBackToRowidRatherThanInventingUniqueIdentities()
    {
        using var source = SqliteImage.Create();
        source.Execute("CREATE TABLE CollectionDynamic(CategoryID TEXT, SubcategoryID TEXT, ItemID TEXT); INSERT INTO CollectionDynamic VALUES('A','B','C'),('A','B','C');");
        using var database = new HogwartsDatabase("synthetic", source.Serialize());
        var table = Assert.Single(database.Tables);
        Assert.Empty(table.IdentityColumns);
        var keys = database.Query(table, new()).Result.Rows.Select(table.RowKey).ToArray();
        Assert.Equal(2, keys.Distinct().Count());
        Assert.All(keys, key => Assert.StartsWith("rowid:", key));
    }

    private static byte[] Image()
    {
        using var database = SqliteImage.Create();
        database.Execute("CREATE TABLE Value(Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO Value VALUES(1,'test');");
        return database.Serialize();
    }
}
