using Microsoft.Data.Sqlite;
using SaveToolkit.Codecs.Sqlite;

namespace SaveToolkit.Codecs.Tests;

public sealed class SqliteRobustnessTests
{
    [Fact]
    public void Empty_database_serializes_and_reopens()
    {
        using var database = SqliteImage.Create();
        byte[] image = database.Serialize();
        using var reopened = SqliteImage.Open(image);
        Assert.Empty(reopened.ListTables());
        Assert.Equal(image, reopened.Serialize());
    }

    [Fact]
    public void Each_public_integrity_check_has_its_own_VM_budget()
    {
        using var database = OpenPopulatedDatabase();
        for (int iteration = 0; iteration < 100; iteration++) database.ValidateIntegrity();
        Assert.Equal(1000L, Assert.Single(database.Query("SELECT count(*) FROM items").Rows)[0]);
    }

    [Fact]
    public void Interrupted_operation_does_not_poison_next_integrity_check()
    {
        using var database = OpenPopulatedDatabase();
        Assert.Throws<SqliteException>(() => database.Query(
            "WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n) SELECT sum(x) FROM n"));
        database.ValidateIntegrity();
        Assert.Equal(1000L, Assert.Single(database.Query("SELECT count(*) FROM items").Rows)[0]);
    }

    private static SqliteImage OpenPopulatedDatabase()
    {
        using var source = SqliteImage.Create();
        source.Execute("CREATE TABLE items (value INTEGER); " +
            "WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<1000) " +
            "INSERT INTO items SELECT x FROM n");
        return SqliteImage.Open(source.Serialize(), new() { MaxVmSteps = 20000 });
    }
}
