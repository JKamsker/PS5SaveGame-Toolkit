namespace SaveToolkit.Codecs.Tests;

public sealed class OodleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(131072)]
    [InlineData(262144)]
    [InlineData(262145)]
    [InlineData(1000000)]
    public void Stored_blocks_round_trip(int length)
    {
        var input = new byte[length];
        new Random(11).NextBytes(input);
        Assert.Equal(input, OodleCodec.Decompress(OodleCodec.EncodeStored(input), length));
    }

    [Fact]
    public void Known_stored_and_memset_vectors()
    {
        Assert.Equal("hello"u8.ToArray(), OodleCodec.Decompress([0x8C, 6, 0, 0, 4, 104, 101, 108, 108, 111], 5));
        Assert.Equal(Enumerable.Repeat((byte)42, 500).ToArray(), OodleCodec.Decompress([0x8C, 6, 7, 255, 255, 42], 500));
    }

    [Fact]
    public void RLE_entropy_vector_decodes_without_LZ_matches()
    {
        // Quantum compressed size 6; entropy type 3, decoded size 64, payload size 1, value 42.
        byte[] stream = [0x8C, 6, 0, 0, 5, 0x30, 0, 0xFC, 0, 1, 42];
        Assert.Equal(Enumerable.Repeat((byte)42, 64).ToArray(), OodleCodec.Decompress(stream, 64));
    }

    [Fact]
    public void Single_symbol_Huffman_vector_has_correct_consumption()
    {
        // Old sparse table: leading 00, symbol count 1, symbol 42, zero bit padding.
        byte[] stream = [0x8C, 6, 0, 0, 7, 0x20, 0, 0xFC, 0, 3, 0, 0x4A, 0x80];
        Assert.Equal(Enumerable.Repeat((byte)42, 64).ToArray(), OodleCodec.Decompress(stream, 64));
    }

    [Fact]
    public void Recursive_entropy_is_capped_before_stack_exhaustion()
    {
        byte[] entropy = EntropyArray(3, [42], 4096);
        for (int depth = 0; depth < 20; depth++)
            entropy = EntropyArray(5, [2, .. entropy, 0, 0, 0], 4096);
        int compressedSize = entropy.Length - 1;
        byte[] stream = [0x8C, 6, (byte)(compressedSize >> 16), (byte)(compressedSize >> 8), (byte)compressedSize, .. entropy];
        var error = Assert.Throws<InvalidDataException>(() => OodleCodec.Decompress(stream, 4096));
        Assert.Contains("nesting", error.Message, StringComparison.Ordinal);
    }

    private static byte[] EntropyArray(int type, byte[] payload, int outputSize)
    {
        uint sizes = ((uint)(outputSize - 1) << 18) | (uint)payload.Length;
        return [(byte)((type << 4) | ((outputSize - 1) >> 14)), (byte)(sizes >> 24),
            (byte)(sizes >> 16), (byte)(sizes >> 8), (byte)sizes, .. payload];
    }

    [Fact]
    public void Rejects_truncation_trailing_data_and_limits()
    {
        Assert.Throws<InvalidDataException>(() => OodleCodec.Decompress([0x8C], 10));
        Assert.Throws<InvalidDataException>(() => OodleCodec.Decompress([0xCC, 6, 1, 2], 1));
        Assert.Throws<InvalidDataException>(() => OodleCodec.Decompress([], 100, 99));
        Assert.Throws<InvalidDataException>(() => OodleCodec.Decompress([0x8C, 10], 10));
    }

    [Fact]
    public void Seeded_corruption_is_bounded_and_has_consistent_errors()
    {
        var random = new Random(28);
        for (int iteration = 0; iteration < 3000; iteration++)
        {
            var input = new byte[random.Next(2, 1024)];
            random.NextBytes(input);
            input[0] = 0x8C;
            input[1] = 6;
            try
            {
                Assert.Equal(4096, OodleCodec.Decompress(input, 4096, 8192).Length);
            }
            catch (InvalidDataException) { }
        }
    }
}
