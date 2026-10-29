using Microsoft.Data.Sqlite;
using SaveToolkit.Codecs.Sqlite;

namespace SaveToolkit.Codecs.Tests;

public sealed class SqliteDiagnosticsTests
{
    [Fact]
    public void Corrupt_B_tree_is_reported_as_invalid_image_with_native_context()
    {
        using var database = SqliteImage.Create();
        database.Execute("CREATE TABLE test (value TEXT)");
        byte[] image = database.Serialize();
        image[100] = 0xFF;

        var error = Assert.Throws<InvalidDataException>(() => SqliteImage.Open(image));

        Assert.Contains("SQLite", error.Message, StringComparison.Ordinal);
        Assert.IsType<SqliteException>(error.InnerException);
    }
}
