namespace SaveToolkit.Codecs.Tests;

public sealed class OodleEntropyTests
{
    public static TheoryData<byte[], byte[]> Vectors => new()
    {
        { [0x8C, 6, 0, 0, 4, 104, 101, 108, 108, 111], "hello"u8.ToArray() },
        { [0x8C, 6, 7, 255, 255, 42], Enumerable.Repeat((byte)42, 500).ToArray() },
        { [0x8C, 6, 0, 0, 5, 0x30, 0, 0xFC, 0, 1, 42], Repeated(42, 64) },
        { [0x8C, 6, 0, 0, 7, 0x20, 0, 0xFC, 0, 3, 0, 0x4A, 0x80], Repeated(42, 64) },
        { OodleTestStreams.Quantum(OodleTestStreams.Entropy(5,
            [2, .. OodleTestStreams.Entropy(3, [42], 32), .. OodleTestStreams.Entropy(3, [43], 32)], 64)),
            [.. Repeated(42, 32), .. Repeated(43, 32)] },
        { OodleTestStreams.Quantum(OodleTestStreams.Entropy(5,
            [0x82, .. OodleTestStreams.Entropy(3, [42], 32), .. OodleTestStreams.Entropy(3, [43], 32),
                2, 0x80, 0x80, 3, 0x51, 0x52, 0, 0, 0], 64)),
            [.. Repeated(42, 32), .. Repeated(43, 32)] },
    };

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Known_vectors_reject_every_truncated_prefix_and_trailing_byte(byte[] stream, byte[] expected)
    {
        byte[] before = stream.ToArray();
        Assert.Equal(expected, OodleCodec.Decompress(stream, expected.Length));
        for (int length = 0; length < stream.Length; length++)
            OodleTestStreams.Reject(stream.AsSpan(0, length).ToArray(), expected.Length);
        OodleTestStreams.Reject([.. stream, 0], expected.Length);
        Assert.Equal(expected, OodleCodec.Decompress(stream, expected.Length));
        Assert.Equal(before, stream);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Multi_array_rejects_missing_interval_bits_with_repaired_lengths(int intervalBytes)
    {
        // Two lengths of 32 require five bits each, read from opposite ends of the interval stream.
        byte[] payload = [0x82, .. OodleTestStreams.Entropy(3, [42], 32),
            .. OodleTestStreams.Entropy(3, [43], 32), (byte)intervalBytes, 0x80,
            0x80, 3, 0x51, 0x52, 0, .. new byte[intervalBytes]];
        OodleTestStreams.Reject(OodleTestStreams.Quantum(OodleTestStreams.Entropy(5, payload, 64)), 64);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Entropy_headers_cannot_expand_past_declared_output(int type)
    {
        byte[] stream = OodleTestStreams.Quantum(OodleTestStreams.Entropy(type, [42], 65));
        OodleTestStreams.Reject(stream, 64);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Missing_entropy_payload_rejected_with_repaired_outer_lengths(int type) =>
        OodleTestStreams.Reject(OodleTestStreams.Quantum(OodleTestStreams.Entropy(type, [], 64)), 64);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Seeded_payload_corruption_reaches_each_entropy_stage(int type)
    {
        var random = new Random(40 + type);
        for (int iteration = 0; iteration < 256; iteration++)
        {
            byte[] payload = new byte[random.Next(2, 80)];
            random.NextBytes(payload);
            byte[] stream = OodleTestStreams.Quantum(OodleTestStreams.Entropy(type, payload, 256));
            try { Assert.Equal(256, OodleCodec.Decompress(stream, 256).Length); }
            catch (InvalidDataException error) { Assert.False(string.IsNullOrWhiteSpace(error.Message)); }
        }
    }

    private static byte[] Repeated(byte value, int length) => Enumerable.Repeat(value, length).ToArray();
}
