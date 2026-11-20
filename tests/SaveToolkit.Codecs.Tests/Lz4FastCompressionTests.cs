using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Encoders;
using SaveToolkit.Sdk.Compression;

namespace SaveToolkit.Codecs.Tests;

/// <summary>
/// The managed fast-level encoder must emit exactly the bytes of K4os' 64-bit engine, which the native
/// Api uses; K4os' 32-bit engine (the one 32-bit WebAssembly gets) differs. K4os is the oracle here, so
/// these tests only mean something in a 64-bit process with <c>LZ4Codec.Enforce32</c> off.
/// </summary>
public sealed class Lz4FastCompressionTests
{
    private static readonly string[] Kinds = ["random", "zeros", "text", "pattern", "mixed"];

    public static TheoryData<string, int> Inputs()
    {
        var data = new TheoryData<string, int>();
        foreach (var kind in Kinds)
        foreach (var size in new[] { 1, 4, 12, 13, 14, 17, 64, 255, 300, 4096, 65_535, 65_536, 65_546, 65_547, 65_548,
            70_000, 140_000, 262_144, 300_000 })
            data.Add(kind, size);
        return data;
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void BlocksMatchK4osByteForByte(string kind, int size)
    {
        AssertSixtyFourBitOracle();
        var input = Lz4HighCompressionTests.Make(kind, size, seed: size);
        var expected = K4os(input, LZ4Codec.MaximumOutputSize(size));
        var actual = Managed(input, Lz4FastCompression.MaximumOutputSize(size));
        Assert.True(expected.SequenceEqual(actual), $"{kind}/{size}: {expected.Length} vs {actual.Length} bytes");
    }

    [Theory]
    [InlineData("text")]
    [InlineData("mixed")]
    [InlineData("pattern")]
    public void MegabyteBlocksMatchK4os(string kind)
    {
        AssertSixtyFourBitOracle();
        foreach (var size in new[] { 1024 * 1024, 1024 * 1024 + 4097, 3 * 1024 * 1024 })
        {
            var input = Lz4HighCompressionTests.Make(kind, size, seed: 11);
            Assert.True(K4os(input, LZ4Codec.MaximumOutputSize(size)).AsSpan()
                .SequenceEqual(Managed(input, Lz4FastCompression.MaximumOutputSize(size))), $"{kind}/{size}");
        }
    }

    [Theory]
    [InlineData("text", 50_000)]
    [InlineData("mixed", 50_000)]
    [InlineData("text", 262_144)]
    [InlineData("mixed", 300_000)]
    [InlineData("random", 2_000)]
    [InlineData("random", 100_000)]
    public void LimitedTargetsMatchK4osIncludingFailure(string kind, int size)
    {
        AssertSixtyFourBitOracle();
        var input = Lz4HighCompressionTests.Make(kind, size, seed: 3);
        var encoded = K4os(input, LZ4Codec.MaximumOutputSize(size)).Length;
        foreach (var capacity in new[] { 0, 1, 16, encoded / 2, encoded - 1, encoded, encoded + 1, LZ4Codec.MaximumOutputSize(size) - 1 })
            Assert.Equal(K4os(input, capacity), Managed(input, capacity));
    }

    [Theory]
    [InlineData(64 * 1024, 0)]
    [InlineData(64 * 1024, 1)]
    [InlineData(256 * 1024, 0)]
    [InlineData(1024 * 1024, 0)]
    [InlineData(4 * 1024 * 1024, 0)]
    public void LinkedBlocksMatchK4osFastChainEncoder(int blockSize, int extraBlocks)
    {
        // Random block lengths stand in for frame flushes (short blocks, small dictionaries), and the
        // window wraps several times, so the prefix, external-dictionary and saved-dictionary paths all run.
        AssertSixtyFourBitOracle();
        var input = Lz4HighCompressionTests.Make("mixed", 2_600_000 + 3 * blockSize, seed: blockSize + extraBlocks);
        Lz4HighCompressionTests.Make("text", 900_000, seed: 4).CopyTo(input, 1_000_000);
        var random = new Random(blockSize);
        using var oracle = new LZ4FastChainEncoder(blockSize, extraBlocks);
        var managed = new ChainDriver(new Lz4FastCompressionChain(), blockSize, extraBlocks);
        var blocks = 0;
        for (var offset = 0; offset < input.Length;)
        {
            var length = Math.Min(input.Length - offset, random.Next(4) == 0 ? random.Next(1, 300) : random.Next(1, blockSize + 1));
            var loaded = oracle.Topup(input, offset, length);
            Assert.Equal(loaded, managed.Topup(input.AsSpan(offset, length)));
            offset += loaded;
            var target = new byte[LZ4Codec.MaximumOutputSize(blockSize)];
            var expected = target[..oracle.Encode(target, 0, target.Length, allowCopy: false)];
            var actual = managed.Encode(LZ4Codec.MaximumOutputSize(blockSize));
            Assert.True(expected.AsSpan().SequenceEqual(actual), $"Block {blocks} at {offset} differs.");
            blocks++;
        }

        Assert.True(blocks > 8);
        Assert.True(managed.SavedDictionaries >= 2, $"The window wrapped {managed.SavedDictionaries} times.");
    }

    [Fact]
    public void LinkedBlocksMatchK4osForTooSmallTargets()
    {
        AssertSixtyFourBitOracle();
        var input = Lz4HighCompressionTests.Make("text", 200_000, seed: 8);
        foreach (var capacity in new[] { 0, 1, 100, 20_000, 30_000 })
        {
            using var oracle = new LZ4FastChainEncoder(64 * 1024);
            var managed = new ChainDriver(new Lz4FastCompressionChain(), 64 * 1024, 0);
            for (var offset = 0; offset < input.Length; offset += 64 * 1024)
            {
                var length = Math.Min(64 * 1024, input.Length - offset);
                oracle.Topup(input, offset, length);
                managed.Topup(input.AsSpan(offset, length));
                var target = new byte[capacity];
                int expected;
                try { expected = oracle.Encode(target, 0, capacity, allowCopy: false); }
                catch (InvalidOperationException) { expected = 0; }
                var actual = managed.Encode(capacity);
                Assert.Equal(expected > 0 ? target[..expected] : [], actual);
                if (expected <= 0) break; // K4os throws and the frame is abandoned.
            }
        }
    }

    [Fact]
    public void EmptyInputAndBoundsFollowTheK4osContract()
    {
        Assert.Equal(0, Lz4FastCompression.Encode([], new byte[16]));
        Assert.Equal(-1, Lz4FastCompression.Encode(new byte[100], new byte[2]));
        Assert.Equal(-1, Lz4FastCompression.Encode(new byte[1], []));
        Assert.Equal(LZ4Codec.MaximumOutputSize(12345), Lz4FastCompression.MaximumOutputSize(12345));
        var chain = new Lz4FastCompressionChain();
        Assert.Throws<ArgumentOutOfRangeException>(() => chain.Compress(new byte[10], 5, 6, new byte[64]));
        Assert.Throws<ArgumentOutOfRangeException>(() => chain.Compress(new byte[10], -1, 1, new byte[64]));
        Assert.Equal(0, chain.SaveDictionary(new byte[10], 100));
    }

    private static void AssertSixtyFourBitOracle()
    {
        Assert.True(Environment.Is64BitProcess, "K4os is only the 64-bit oracle in a 64-bit process.");
        Assert.False(LZ4Codec.Enforce32);
    }

    private static byte[] K4os(byte[] input, int capacity)
    {
        var target = new byte[capacity];
        var length = LZ4Codec.Encode(input, target);
        return length < 0 ? [] : target[..length];
    }

    private static byte[] Managed(byte[] input, int capacity)
    {
        var target = new byte[capacity];
        var length = Lz4FastCompression.Encode(input, target);
        return length < 0 ? [] : target[..length];
    }

    /// <summary>K4os <c>LZ4EncoderBase</c>'s input ring buffer, driving a managed chain.</summary>
    internal sealed class ChainDriver(ILz4CompressionChain chain, int blockSize, int extraBlocks)
    {
        private readonly byte[] _input = new byte[64 * 1024 + (1 + extraBlocks) * blockSize + 32 + 8];
        private readonly int _inputLength = 64 * 1024 + (1 + extraBlocks) * blockSize + 32;
        private int _inputIndex;
        private int _inputPointer;

        public int SavedDictionaries { get; private set; }

        public int Topup(ReadOnlySpan<byte> source)
        {
            var chunk = Math.Min(Math.Max(_inputIndex + blockSize - _inputPointer, 0), source.Length);
            source[..chunk].CopyTo(_input.AsSpan(_inputPointer));
            _inputPointer += chunk;
            return chunk;
        }

        /// <summary>Encodes the pending block; empty when the target cannot hold it.</summary>
        public byte[] Encode(int capacity)
        {
            var length = _inputPointer - _inputIndex;
            if (length <= 0) return [];
            var target = new byte[capacity];
            var encoded = chain.Compress(_input, _inputIndex, length, target);
            if (encoded <= 0) return [];
            _inputIndex = _inputPointer;
            if (_inputIndex + blockSize > _inputLength)
            {
                _inputIndex = _inputPointer = chain.SaveDictionary(_input, _inputPointer);
                SavedDictionaries++;
            }

            return target[..encoded];
        }
    }
}
