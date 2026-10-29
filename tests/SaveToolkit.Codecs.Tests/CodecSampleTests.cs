using System.Buffers.Binary;
using SaveToolkit.Codecs.Sqlite;

namespace SaveToolkit.Codecs.Tests;

public sealed class CodecSampleTests
{
    [Fact]
    public void AtomicHeart_actual_chunks_decode()
    {
        byte[] file = SampleChunks.Read("PPSA10695", "ue4savegame.dpx.sav");
        var decoded = SampleChunks.DecodeFrames(file, 2302, 14);
        Assert.Equal(1751391, decoded.Bytes.Length);
        Assert.Equal(file.Length - 20, decoded.End);
        Assert.Equal(decoded.Bytes, OodleCodec.Decompress(OodleCodec.EncodeStored(decoded.Bytes), decoded.Bytes.Length));
        int compressedLength = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(2302 + 16)));
        var random = new Random(11);
        byte[] firstChunk = file.AsSpan(2302 + 48, compressedLength).ToArray();
        for (int iteration = 0; iteration < 100; iteration++)
        {
            byte[] mutated = firstChunk.ToArray();
            mutated[random.Next(5, mutated.Length)] ^= (byte)random.Next(1, 256);
            try { Assert.Equal(131072, OodleCodec.Decompress(mutated, 131072).Length); }
            catch (InvalidDataException) { }
        }
        for (int length = 0; length < firstChunk.Length; length += 101)
            Assert.Throws<InvalidDataException>(() => OodleCodec.Decompress(firstChunk.AsSpan(0, length), 131072));
    }

    [Fact]
    public void Hogwarts_actual_database_images_decode()
    {
        byte[] file = SampleChunks.Read("PPSA01603", "ue4savegame.ps5.sav", "sdimg_HL0010");
        int start = SampleChunks.FindFrames(file, 0);
        var primary = SampleChunks.DecodeFrames(file, start, 35);
        Assert.Equal(4534280, primary.Bytes.Length);
        int next = SampleChunks.FindFrames(file, primary.End);
        var exclusive = SampleChunks.DecodeFrames(file, next, 33);
        Assert.Equal(4231176, exclusive.Bytes.Length);
        Assert.Equal(primary.Bytes, OodleCodec.Decompress(OodleCodec.EncodeStored(primary.Bytes), primary.Bytes.Length));
        Assert.Equal(exclusive.Bytes, OodleCodec.Decompress(OodleCodec.EncodeStored(exclusive.Bytes), exclusive.Bytes.Length));
        Assert.Equal("SQLite format 3\0"u8.ToArray(), primary.Bytes.AsSpan(8, 16).ToArray());
        Assert.Equal("SQLite format 3\0"u8.ToArray(), exclusive.Bytes.AsSpan(8, 16).ToArray());
        ValidateDatabase(primary.Bytes);
        ValidateDatabase(exclusive.Bytes);
    }

    private static void ValidateDatabase(byte[] image)
    {
        Assert.Equal(image.Length - 4, BinaryPrimitives.ReadInt32LittleEndian(image));
        Assert.Equal(image.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(4)));
        using var database = SqliteImage.Open(image.AsSpan(8));
        var tables = database.ListTables();
        Assert.NotEmpty(tables);
        Assert.Contains("MiscDataDynamic", tables);
        var row = database.Query("SELECT * FROM MiscDataDynamic LIMIT 1");
        Assert.NotEmpty(row.Columns);
        Assert.Single(row.Rows);
        byte[] serialized = database.Serialize();
        Assert.Equal(image.AsSpan(8).ToArray(), serialized);
        using var reopened = SqliteImage.Open(serialized);
        Assert.Equal(tables, reopened.ListTables());
    }
}
