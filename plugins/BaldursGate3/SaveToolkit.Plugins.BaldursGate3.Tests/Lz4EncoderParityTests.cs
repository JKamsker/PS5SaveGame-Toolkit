using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using SaveToolkit.Sdk.Compression;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

/// <summary>
/// Blocks and frames go through the SDK's managed LZ4 ports (K4os' HC encoder traps the browser runtime
/// and its fast level is the 32-bit engine there). Written packages and LSF sections must keep the exact
/// bytes of K4os' 64-bit engine, which this 64-bit test process runs.
/// </summary>
public sealed class Lz4EncoderParityTests
{
    private const byte Lz4 = (byte)LarianCompressionMethod.Lz4;

    public static TheoryData<byte, int> Cases()
    {
        var data = new TheoryData<byte, int>();
        foreach (var level in new byte[] { 0x00, 0x10, 0x20, 0x40 })
        foreach (var size in new[] { 1, 100, 65_535, 65_536, 65_537, 65_547, 131_105, 262_144, 300_000 })
            data.Add(level, size);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BlocksAndFramesMatchK4os(byte level, int size)
    {
        Assert.True(Environment.Is64BitProcess && !LZ4Codec.Enforce32, "K4os is the 64-bit oracle only in a 64-bit process.");
        var source = Input(size);
        var flags = (byte)(Lz4 | level);
        var lz4Level = level switch { 0x10 => LZ4Level.L00_FAST, 0x40 => LZ4Level.L12_MAX, _ => LZ4Level.L10_OPT };

        var block = new byte[LZ4Codec.MaximumOutputSize(size)];
        var blockLength = LZ4Codec.Encode(source, block, lz4Level);
        Assert.Equal(block[..blockLength], LarianCompression.Encode(source, flags));

        using var frame = new MemoryStream();
        using (var encoder = LZ4Stream.Encode(frame, new LZ4EncoderSettings { CompressionLevel = lz4Level }, leaveOpen: true))
            encoder.Write(source);
        var framed = LarianCompression.Encode(source, flags, framed: true);
        Assert.Equal(frame.ToArray(), framed);
        Assert.Equal(source, LarianCompression.Decode(framed, size, flags, framed: true));
    }

    [Fact]
    public void ChainEncoderMatchesK4osForPiecewiseWrites()
    {
        // Partial blocks and ring-buffer wrap-around (the dictionary is moved after every second block).
        var source = Input(700_000);
        foreach (var level in new[] { LZ4Level.L00_FAST, LZ4Level.L03_HC, LZ4Level.L09_HC, LZ4Level.L11_OPT, LZ4Level.L12_MAX })
        {
            var settings = new LZ4EncoderSettings { CompressionLevel = level };
            ILz4CompressionChain Chain() => level == LZ4Level.L00_FAST ? new Lz4FastCompressionChain() : new Lz4HighCompressionChain((int)level);
            var expected = WritePieces(source, output => LZ4Stream.Encode(output, settings, leaveOpen: true));
            var actual = WritePieces(source, output => new LZ4EncoderStream(output, settings.CreateDescriptor(),
                descriptor => new ManagedLz4ChainEncoder(Chain(), descriptor.BlockSize, extraBlocks: 0), leaveOpen: true));
            Assert.True(expected.AsSpan().SequenceEqual(actual), $"Frame differs at {level}.");
        }
    }

    private static byte[] WritePieces(byte[] source, Func<Stream, Stream> open)
    {
        var random = new Random(5);
        using var output = new MemoryStream();
        using (var stream = open(output))
        {
            for (var offset = 0; offset < source.Length;)
            {
                var count = Math.Min(source.Length - offset, random.Next(1, 90_000));
                stream.Write(source, offset, count);
                offset += count;
            }
        }

        return output.ToArray();
    }

    private static byte[] Input(int size)
    {
        var random = new Random(size);
        var data = new byte[size];
        string[] words = ["<attribute id=\"", "\" type=\"FixedString\" value=\"", "Gold", "Shadowheart", "\"/>\n", "\0\0\0\0"];
        for (var i = 0; i < size;)
        {
            if (random.Next(6) == 0)
            {
                data[i++] = (byte)random.Next(256);
                continue;
            }

            foreach (var c in words[random.Next(words.Length)])
                if (i < size) data[i++] = (byte)c;
        }

        return data;
    }
}
