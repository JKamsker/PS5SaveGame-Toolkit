using Microsoft.Data.Sqlite;
using SaveToolkit.Codecs.Sqlite;

namespace SaveToolkit.Codecs.Tests;

public sealed class SqliteImageTests
{
    [Fact]
    public void Creates_queries_edits_serializes_and_reopens_database()
    {
        using var database = SqliteImage.Create();
        database.Execute("CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT, quantity INTEGER, payload BLOB);");
        database.Execute("INSERT INTO items VALUES (1, $name, 3, $blob)",
            new Dictionary<string, object?> { ["$name"] = "Potion", ["$blob"] = new byte[] { 1, 2, 3 } });
        byte[] original = database.Serialize();
        using var reopened = SqliteImage.Open(original);
        Assert.Equal(["items"], reopened.ListTables());
        var row = Assert.Single(reopened.Query("SELECT name, quantity, payload, NULL FROM items WHERE id=1").Rows);
        Assert.Equal("Potion", row[0]);
        Assert.Equal(3L, row[1]);
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<byte[]>(row[2]));
        Assert.Null(row[3]);
        reopened.Execute("UPDATE items SET quantity=$count WHERE id=1", new Dictionary<string, object?> { ["$count"] = 42 });
        byte[] edited = reopened.Serialize();
        using var editedDatabase = SqliteImage.Open(edited);
        Assert.Equal(42L, Assert.Single(editedDatabase.Query("SELECT quantity FROM items").Rows)[0]);
        editedDatabase.ValidateIntegrity();
        Assert.Equal(original, database.Serialize());
    }

    [Theory]
    [InlineData("ATTACH DATABASE 'should-never-exist.db' AS external")]
    [InlineData("VACUUM INTO 'should-never-exist.db'")]
    [InlineData("PRAGMA temp_store=FILE")]
    [InlineData("PRAGMA journal_mode=WAL")]
    [InlineData("SELECT load_extension('should-never-load')")]
    [InlineData("BEGIN")]
    public void Denies_SQL_that_could_escape_memory_only_contract(string sql)
    {
        using var database = SqliteImage.Create();
        Assert.Throws<SqliteException>(() => database.Execute(sql));
    }

    [Fact]
    public void Query_is_read_only_and_failed_batch_rolls_back()
    {
        using var database = SqliteImage.Create();
        database.Execute("CREATE TABLE counts (quantity INTEGER CHECK(quantity>0)); INSERT INTO counts VALUES(1)");
        Assert.Throws<SqliteException>(() => database.Query("UPDATE counts SET quantity=2 RETURNING quantity"));
        Assert.Throws<SqliteException>(() => database.Execute("UPDATE counts SET quantity=2; INSERT INTO counts VALUES(-1)"));
        Assert.Equal(1L, Assert.Single(database.Query("SELECT quantity FROM counts").Rows)[0]);
    }

    [Fact]
    public void Bounds_rows_results_VM_work_and_image_growth()
    {
        var limits = new SqliteLimits { MaxRows = 1, MaxResultBytes = 1024, MaxValueBytes = 1024, MaxVmSteps = 2000, MaxDatabaseBytes = 16384 };
        using var database = SqliteImage.Create(limits);
        Assert.Throws<InvalidDataException>(() => database.Query("SELECT 1 UNION ALL SELECT 2"));
        Assert.Throws<InvalidDataException>(() => database.Query("SELECT randomblob(1000), randomblob(1000)"));
        Assert.Throws<SqliteException>(() => database.Query(
            "WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n) SELECT sum(x) FROM n"));
        database.Execute("CREATE TABLE blobs (data BLOB)");
        Assert.Throws<SqliteException>(() => database.Execute(
            "WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<30) INSERT INTO blobs SELECT zeroblob(800) FROM n"));
        Assert.Empty(database.Query("SELECT * FROM blobs").Rows);
        Assert.True(database.Serialize().Length <= limits.MaxDatabaseBytes);
    }

    [Fact]
    public void Rejects_corruption_truncation_WAL_and_oversize_images()
    {
        Assert.Throws<InvalidDataException>(() => SqliteImage.Open([]));
        using var database = SqliteImage.Create();
        database.Execute("CREATE TABLE test (value TEXT)");
        byte[] good = database.Serialize();
        Assert.Throws<InvalidDataException>(() => SqliteImage.Open(good.AsSpan(0, good.Length - 1)));
        byte[] wal = good.ToArray();
        wal[18] = wal[19] = 2;
        Assert.Throws<InvalidDataException>(() => SqliteImage.Open(wal));
        Assert.Throws<InvalidDataException>(() => SqliteImage.Open(good, new SqliteLimits { MaxDatabaseBytes = 4096 }));
        byte[] corrupt = good.ToArray();
        corrupt[100] = 0xFF; // Invalid root B-tree page kind.
        Exception error = Assert.ThrowsAny<Exception>(() => SqliteImage.Open(corrupt));
        Assert.True(error is InvalidDataException or SqliteException);
    }

    [Fact]
    public void Seeded_corruption_and_repeated_lifetimes_are_bounded()
    {
        using var database = SqliteImage.Create();
        database.Execute("CREATE TABLE test (value TEXT); INSERT INTO test VALUES('hello')");
        byte[] original = database.Serialize();
        var random = new Random(28);
        for (int iteration = 0; iteration < 100; iteration++)
        {
            byte[] mutated = original.ToArray();
            for (int mutation = 0; mutation < 4; mutation++) mutated[random.Next(mutated.Length)] ^= (byte)random.Next(1, 256);
            try
            {
                using var opened = SqliteImage.Open(mutated, new SqliteLimits { MaxDatabaseBytes = 32768, MaxVmSteps = 100_000 });
                opened.ValidateIntegrity();
            }
            catch (Exception error) when (error is InvalidDataException or SqliteException) { }
        }
    }
}
