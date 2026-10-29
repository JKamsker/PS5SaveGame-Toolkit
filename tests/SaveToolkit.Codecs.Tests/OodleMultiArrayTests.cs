namespace SaveToolkit.Codecs.Tests;

public sealed class OodleMultiArrayTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nonzero_asymmetric_interval_streams_decode_exactly(bool combinedIndexes)
    {
        byte[] expected = [.. Repeat(42, 33), .. Repeat(43, 34), .. Repeat(42, 35)];
        byte[] stream = Build(combinedIndexes, [0x08, 0xC0, 0x10]);
        Assert.Equal(expected, OodleCodec.Decompress(stream, expected.Length));
        Assert.Equal(expected, OodleCodec.Decompress(OodleCodec.EncodeStored(expected), expected.Length));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void Repaired_interval_truncation_cannot_read_missing_bits(bool combinedIndexes, int intervalBytes) =>
        OodleTestStreams.Reject(Build(combinedIndexes, new byte[intervalBytes]), 102);

    private static byte[] Build(bool combinedIndexes, byte[] intervalBytes)
    {
        // Three intervals use lengths 33, 34 and 35. Forward remainders 00001/00011
        // need two bytes; backward remainder 00010 needs one. Combined mode adds a
        // zero-width ignored length for the terminator; separate mode ends on a forward length.
        byte[] indexes = combinedIndexes
            ? [0x80, 4, 0x51, 0x52, 0x51, 0]
            : [0x80, 4, 1, 2, 1, 0, 0x80, 3, 5, 5, 5];
        byte[] payload = [0x82, .. OodleTestStreams.Entropy(3, [42], 68),
            .. OodleTestStreams.Entropy(3, [43], 34),
            (byte)intervalBytes.Length, combinedIndexes ? (byte)0x80 : (byte)0,
            .. indexes, .. intervalBytes];
        return OodleTestStreams.Quantum(OodleTestStreams.Entropy(5, payload, 102));
    }

    private static byte[] Repeat(byte value, int length) => Enumerable.Repeat(value, length).ToArray();
}
