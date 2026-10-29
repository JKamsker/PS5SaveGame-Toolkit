using System.Buffers.Binary;
using System.IO.Compression;
using SaveToolkit.Plugins.UnrealGvas.Gvas;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasFixtureCompatibilityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void WrapperFixturesKeepLegacyCompressionAndExactHeaderBytes(bool astroneer, bool empty)
    {
        byte[] plain = empty ? [] : GvasFixture.Primitives();
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(plain);
        byte[] expected = output.ToArray();
        if (astroneer)
        {
            byte[] prefix = [0xBE, 0x40, 0x37, 0x4A, 0xEE, 0x0B, 0x74, 0xA3, 1, 0, 0, 0, 0, 0, 0, 0];
            BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(12), plain.Length);
            expected = [.. prefix, .. expected];
        }
        byte[] actual = GvasFixture.Wrap(plain, astroneer);
        Assert.Equal(expected, actual);
        if (!empty) Assert.Equal(plain, GvasEnvelope.Open(actual).File.Serialize());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActorFixturesKeepTheirSmallestSizeCompression(bool empty)
    {
        byte[] plain = empty ? [] : TitleFixture.ActorProperties();
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) zlib.Write(plain);
        byte[] actual = GvasFixture.Compress(plain, CompressionLevel.SmallestSize);
        Assert.Equal(output.ToArray(), actual);
        using var input = new MemoryStream(actual);
        using var decoder = new ZLibStream(input, CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        decoder.CopyTo(decoded);
        Assert.Equal(plain, decoded.ToArray());
    }
}
