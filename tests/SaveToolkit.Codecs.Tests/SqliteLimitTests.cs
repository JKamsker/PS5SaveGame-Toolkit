using Microsoft.Data.Sqlite;
using SaveToolkit.Codecs.Sqlite;

namespace SaveToolkit.Codecs.Tests;

public sealed class SqliteLimitTests
{
    [Theory]
    [InlineData("SELECT ", ",", 257, "1")]
    [InlineData("SELECT ", " UNION ALL SELECT ", 33, "1")]
    [InlineData("SELECT ", "+", 66, "1")]
    public void Native_column_compound_and_expression_limits_are_installed(
        string prefix, string separator, int count, string item)
    {
        using var database = SqliteImage.Create();
        string sql = prefix + string.Join(separator, Enumerable.Repeat(item, count));
        Assert.Throws<SqliteException>(() => database.Query(sql));
        Assert.Equal(42L, Assert.Single(database.Query("SELECT 42").Rows)[0]);
    }

    [Fact]
    public void SQL_parameter_and_value_limits_reject_then_allow_subsequent_work()
    {
        using var database = SqliteImage.Create(new() { MaxSqlBytes = 256, MaxValueBytes = 1024 });
        Assert.Throws<InvalidDataException>(() => database.Query("SELECT 1 --" + new string('x', 256)));
        Assert.Throws<InvalidDataException>(() => database.Query("SELECT $value",
            new Dictionary<string, object?> { ["$value"] = new byte[1025] }));
        Assert.Throws<InvalidDataException>(() => database.Query("SELECT $value",
            new Dictionary<string, object?> { ["$value"] = new string('\u20AC', 342) }));
        Assert.Throws<InvalidDataException>(() => database.Query("SELECT 1",
            Enumerable.Range(0, 257).ToDictionary(index => "$p" + index, _ => (object?)1)));
        Assert.Throws<SqliteException>(() => database.Query("SELECT zeroblob(1025)"));
        byte[] expected = Enumerable.Repeat((byte)42, 1024).ToArray();
        Assert.Equal(expected, Assert.IsType<byte[]>(Assert.Single(database.Query("SELECT $value",
            new Dictionary<string, object?> { ["$value"] = expected }).Rows)[0]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Created_and_imported_databases_enforce_growth_limit_and_rollback(bool imported)
    {
        var limits = new SqliteLimits { MaxDatabaseBytes = 16384 };
        using var source = SqliteImage.Create(limits);
        source.Execute("CREATE TABLE items (data BLOB)");
        using var reopened = imported ? SqliteImage.Open(source.Serialize(), limits) : null;
        SqliteImage database = reopened ?? source;
        bool rejected = false;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            byte[] before = database.Serialize();
            try { database.Execute("INSERT INTO items VALUES (zeroblob(1000))"); }
            catch (SqliteException error)
            {
                Assert.Equal(13, error.SqliteErrorCode); // SQLITE_FULL
                Assert.Equal(before, database.Serialize());
                rejected = true;
                break;
            }
        }
        Assert.True(rejected, "Database growth must stop at the configured page/image capacity.");
        Assert.InRange(database.Serialize().Length, 4096, limits.MaxDatabaseBytes);
        database.ValidateIntegrity();
    }

    [Fact]
    public void Result_limit_counts_column_names_and_UTF8_bytes()
    {
        using var database = SqliteImage.Create(new() { MaxResultBytes = 1024, MaxValueBytes = 1024 });
        Assert.Throws<InvalidDataException>(() => database.Query(
            "SELECT 1 AS \"" + new string('x', 600) + "\", 2 AS \"" + new string('y', 600) + "\""));
        Assert.Throws<InvalidDataException>(() => database.Query("SELECT $value AS v, $value AS w",
            new Dictionary<string, object?> { ["$value"] = new string('\u20AC', 171) }));
        Assert.Equal(42L, Assert.Single(database.Query("SELECT 42").Rows)[0]);
    }

    [Fact]
    public void Query_batches_cannot_mutate_and_errors_leave_authorization_usable()
    {
        using var database = SqliteImage.Create();
        database.Execute("CREATE TABLE items (value INTEGER); INSERT INTO items VALUES (42)");
        Assert.Throws<SqliteException>(() => database.Query("SELECT 1; DELETE FROM items"));
        Assert.Throws<InvalidDataException>(() => database.Query("SELECT 1; SELECT 2"));
        database.Execute("UPDATE items SET value=43");
        Assert.Equal(43L, Assert.Single(database.Query("SELECT value FROM items").Rows)[0]);
    }
}
