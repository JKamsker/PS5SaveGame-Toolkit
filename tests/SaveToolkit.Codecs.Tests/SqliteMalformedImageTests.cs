using System.Buffers.Binary;
using SaveToolkit.Codecs.Sqlite;

namespace SaveToolkit.Codecs.Tests;

public sealed class SqliteMalformedImageTests
{
    [Fact]
    public void Header_prefixes_and_page_aligned_truncation_reject_cleanly()
    {
        byte[] image = CreateImage();
        int pageSize = BinaryPrimitives.ReadUInt16BigEndian(image.AsSpan(16));
        for (int length = 0; length < 100; length++) Reject(image.AsSpan(0, length).ToArray());
        for (int length = pageSize; length < image.Length; length += pageSize)
        {
            Reject(image.AsSpan(0, length - 1).ToArray());
            Reject(image.AsSpan(0, length).ToArray());
            Reject(image.AsSpan(0, length + 1).ToArray());
        }
        Reject(image.AsSpan(0, image.Length - 1).ToArray());
        using var good = SqliteImage.Open(image);
        Assert.Equal(8L, Assert.Single(good.Query("SELECT count(*) FROM items").Rows)[0]);
        Assert.Equal(image, good.Serialize());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(16, 0)]
    [InlineData(17, 1)]
    [InlineData(18, 2)]
    [InlineData(19, 2)]
    [InlineData(100, 0xFF)]
    public void Independent_header_and_B_tree_corruptions_reject_cleanly(int offset, byte value)
    {
        byte[] image = CreateImage();
        image[offset] = value;
        Reject(image);
    }

    [Fact]
    public void Image_is_rejected_below_size_limit_and_accepted_at_exact_limit()
    {
        byte[] image = CreateImage();
        Assert.Throws<InvalidDataException>(() => SqliteImage.Open(image,
            new() { MaxDatabaseBytes = image.Length - 1 }));
        using var database = SqliteImage.Open(image, new() { MaxDatabaseBytes = image.Length });
        Assert.Equal(image, database.Serialize());
    }

    private static byte[] CreateImage()
    {
        using var database = SqliteImage.Create();
        database.Execute("CREATE TABLE items (data BLOB); " +
            "WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<8) " +
            "INSERT INTO items SELECT zeroblob(2000) FROM n");
        return database.Serialize();
    }

    private static void Reject(byte[] image)
    {
        var error = Assert.Throws<InvalidDataException>(() => SqliteImage.Open(image,
            new() { MaxDatabaseBytes = 32768, MaxVmSteps = 100000 }));
        Assert.Contains("SQLite", error.Message, StringComparison.Ordinal);
    }
}
