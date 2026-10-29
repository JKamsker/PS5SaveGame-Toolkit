using System.Buffers.Binary;
using SaveToolkit.Codecs;
using SaveToolkit.Codecs.Sqlite;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsDecodeAllocationTests
{
    [Fact]
    public void MultiFrameDecodeAllocatesOnlyTheResultAndBoundedCodecBuffers()
    {
        using var source = SqliteImage.Create();
        source.Execute("""
            CREATE TABLE Payload(Bytes BLOB);
            WITH RECURSIVE chunks(n) AS (VALUES(0) UNION ALL SELECT n+1 FROM chunks WHERE n<7)
            INSERT INTO Payload SELECT zeroblob(524288) FROM chunks;
            """);
        byte[] image = source.Serialize();
        byte[] frames = HogwartsImage.EncodeStored(image);
        _ = HogwartsImage.Decode(frames); // Exclude one-time codec initialization.
        long before = GC.GetAllocatedBytesForCurrentThread();
        byte[] decoded = HogwartsImage.Decode(frames);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // Codec input, padded output, returned chunk and final image, plus bounded per-frame overhead.
        Assert.True(allocated < 5L * image.Length, $"Allocated {allocated:N0} bytes for a {image.Length:N0}-byte image.");
        Assert.Equal(image, decoded);
    }

    [Fact]
    public void InvalidLaterFrameIsRejectedBeforeAnyChunkIsDecoded()
    {
        using var source = SqliteImage.Create();
        source.Execute("CREATE TABLE Payload(Bytes BLOB); INSERT INTO Payload VALUES(zeroblob(300000));");
        byte[] frames = HogwartsImage.EncodeStored(source.Serialize());
        int nextFrame = HogwartsImage.FrameBytes + checked((int)BinaryPrimitives.ReadUInt64LittleEndian(frames.AsSpan(16)));
        BinaryPrimitives.WriteUInt64LittleEndian(frames.AsSpan(nextFrame), ulong.MaxValue);
        Assert.Throws<SaveFormatException>(() => HogwartsImage.Decode([])); // Warm exception path.
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<SaveFormatException>(() => HogwartsImage.Decode(frames));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < HogwartsImage.ChunkBytes, $"Allocated {allocated:N0} bytes before rejecting frame declarations.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void PrefixCanCrossIndependentFrameBoundaries(int firstLength)
    {
        using var source = SqliteImage.Create();
        source.Execute("CREATE TABLE Value(Id INTEGER PRIMARY KEY);");
        byte[] image = source.Serialize();
        byte[] original = HogwartsImage.EncodeStored(image);
        // This small image has one stored Kraken frame (two-byte codec header).
        byte[] plain = original[(HogwartsImage.FrameBytes + 2)..];
        using var stream = new MemoryStream();
        WriteFrame(stream, original, plain.AsSpan(0, firstLength));
        WriteFrame(stream, original, plain.AsSpan(firstLength));
        Assert.Equal(image, HogwartsImage.Decode(stream.ToArray()));
    }

    private static void WriteFrame(Stream stream, byte[] original, ReadOnlySpan<byte> plain)
    {
        byte[] encoded = OodleCodec.EncodeStored(plain);
        byte[] header = original[..HogwartsImage.FrameBytes];
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), (ulong)encoded.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(24), (ulong)plain.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(32), (ulong)encoded.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(40), (ulong)plain.Length);
        stream.Write(header);
        stream.Write(encoded);
    }
}
