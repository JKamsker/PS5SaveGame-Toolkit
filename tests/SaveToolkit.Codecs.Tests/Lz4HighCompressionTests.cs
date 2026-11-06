using K4os.Compression.LZ4;
using SaveToolkit.Sdk.Compression;

namespace SaveToolkit.Codecs.Tests;

/// <summary>
/// The managed HC encoder must emit exactly K4os' bytes: saves that plugins wrote with K4os before
/// (and the native Api still writes) must not change. K4os is the test oracle here.
/// </summary>
public sealed class Lz4HighCompressionTests
{
    public static TheoryData<string, int> Inputs()
    {
        var data = new TheoryData<string, int>();
        foreach (var kind in Kinds)
        foreach (var size in new[] { 1, 4, 12, 13, 17, 64, 255, 300, 4096, 65535, 65536, 65537, 140_000 })
            data.Add(kind, size);
        return data;
    }

    private static readonly string[] Kinds = ["random", "zeros", "text", "pattern", "mixed"];

    [Theory]
    [MemberData(nameof(Inputs))]
    public void EveryLevelMatchesK4osByteForByte(string kind, int size)
    {
        var input = Make(kind, size, seed: size);
        for (var level = Lz4HighCompression.MinimumLevel; level <= Lz4HighCompression.MaximumLevel; level++)
        {
            var expected = K4os(input, LZ4Codec.MaximumOutputSize(size), level);
            var actual = Managed(input, Lz4HighCompression.MaximumOutputSize(size), level);
            Assert.True(expected.SequenceEqual(actual), $"{kind}/{size} at level {level}: {expected.Length} vs {actual.Length} bytes");
        }
    }

    [Theory]
    [InlineData("text", 50_000)]
    [InlineData("mixed", 50_000)]
    [InlineData("random", 2_000)]
    public void LimitedTargetsMatchK4osIncludingFailure(string kind, int size)
    {
        var input = Make(kind, size, seed: 3);
        foreach (var level in new[] { 3, 9, 10, 12 })
        {
            var encoded = K4os(input, LZ4Codec.MaximumOutputSize(size), level).Length;
            foreach (var capacity in new[] { encoded - 1, encoded, encoded + 1, encoded / 2 })
                Assert.Equal(K4os(input, capacity, level), Managed(input, capacity, level));
        }
    }

    [Fact]
    public void LargeWitcherSizedChunkMatchesK4osAtTheMaximumLevel()
    {
        // Witcher 3 writes 1 MiB chunks at L12_MAX.
        var input = Make("text", 1024 * 1024, seed: 9);
        var expected = K4os(input, LZ4Codec.MaximumOutputSize(input.Length), 12);
        Assert.Equal(expected, Managed(input, Lz4HighCompression.MaximumOutputSize(input.Length), 12));
    }

    [Fact]
    public void EmptyInputAndBadLevelsFollowTheK4osContract()
    {
        Assert.Equal(0, Lz4HighCompression.Encode([], new byte[16], 12));
        Assert.Equal(-1, Lz4HighCompression.Encode(new byte[100], new byte[2], 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => Lz4HighCompression.Encode(new byte[1], new byte[32], 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Lz4HighCompression.Encode(new byte[1], new byte[32], 13));
        Assert.Equal(LZ4Codec.MaximumOutputSize(12345), Lz4HighCompression.MaximumOutputSize(12345));
    }

    [Fact]
    public void ChainRejectsBlocksOutsideTheWindow()
    {
        var chain = new Lz4HighCompressionChain(12);
        Assert.Throws<ArgumentOutOfRangeException>(() => chain.Compress(new byte[10], 5, 6, new byte[64]));
        Assert.Throws<ArgumentOutOfRangeException>(() => chain.Compress(new byte[10], -1, 1, new byte[64]));
    }

    private static byte[] K4os(byte[] input, int capacity, int level)
    {
        var target = new byte[capacity];
        var length = LZ4Codec.Encode(input, target, (LZ4Level)level);
        return length < 0 ? [] : target[..length];
    }

    private static byte[] Managed(byte[] input, int capacity, int level)
    {
        var target = new byte[capacity];
        var length = Lz4HighCompression.Encode(input, target, level);
        return length < 0 ? [] : target[..length];
    }

    internal static byte[] Make(string kind, int size, int seed)
    {
        var random = new Random(seed);
        var data = new byte[size];
        switch (kind)
        {
            case "random":
                random.NextBytes(data);
                break;
            case "zeros":
                break;
            case "pattern":
                for (var i = 0; i < size; i++) data[i] = (byte)("abcabcabd"[i % 9] + i / 5000 % 3);
                break;
            case "text":
                string[] words = ["Geralt ", "of ", "Rivia ", "<item id=\"", "\"/>\n", "quantity=", "0000", "\0\0\0\0", "\x01\x02\x01\x02"];
                for (var i = 0; i < size;)
                    foreach (var c in words[random.Next(words.Length)])
                        if (i < size) data[i++] = (byte)c;
                break;
            default:
                for (var i = 0; i < size; i++) data[i] = random.Next(3) switch { 0 => (byte)random.Next(256), 1 => (byte)(i % 7), _ => (byte)' ' };
                for (var i = 0; i + 300 < size; i += 997) Array.Fill(data, (byte)0xAB, i, random.Next(1, 300));
                break;
        }

        return data;
    }
}
