namespace SaveToolkit.Codecs.Tests;

public sealed class OodleFramingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(131071)]
    [InlineData(131072)]
    [InlineData(131073)]
    [InlineData(262143)]
    [InlineData(262144)]
    [InlineData(262145)]
    [InlineData(524288)]
    [InlineData(524289)]
    public void Stored_emission_matches_independent_block_layout_and_exact_limit(int length)
    {
        byte[] input = new byte[length];
        new Random(40).NextBytes(input);
        byte[] before = input.ToArray();
        int limit = Math.Max(length, 1);
        byte[] stream = OodleCodec.EncodeStored(input, limit);
        int blocks = (length + OodleTestStreams.QuantumBytes - 1) / OodleTestStreams.QuantumBytes;
        Assert.Equal(length + 2 * blocks, stream.Length);
        int cursor = 0;
        for (int offset = 0; offset < length; offset += OodleTestStreams.QuantumBytes)
        {
            Assert.Equal(0xCC, stream[cursor++]);
            Assert.Equal(6, stream[cursor++]);
            int count = Math.Min(OodleTestStreams.QuantumBytes, length - offset);
            Assert.Equal(input.AsSpan(offset, count).ToArray(), stream.AsSpan(cursor, count).ToArray());
            cursor += count;
        }
        Assert.Equal(stream.Length, cursor);
        Assert.Equal(input, OodleCodec.Decompress(stream, length, limit));
        Assert.Equal(before, input);
        if (length > 1)
            Assert.Throws<InvalidDataException>(() => OodleCodec.EncodeStored(input, length - 1));
    }

    [Fact]
    public void Every_stored_prefix_and_block_header_boundary_is_rejected()
    {
        byte[] small = OodleCodec.EncodeStored("stored payload"u8);
        for (int length = 0; length < small.Length; length++)
            OodleTestStreams.Reject(small.AsSpan(0, length).ToArray(), 14);

        byte[] stream = OodleCodec.EncodeStored(new byte[OodleTestStreams.QuantumBytes + 9]);
        foreach (int cut in new[] { 0, 1, 2, 262145, 262146, 262147, 262148, stream.Length - 1 })
            OodleTestStreams.Reject(stream.AsSpan(0, cut).ToArray(), OodleTestStreams.QuantumBytes + 9);
        foreach (int header in new[] { 0, OodleTestStreams.QuantumBytes + 2 })
        {
            byte[] corrupt = stream.ToArray();
            corrupt[header] = 0;
            OodleTestStreams.Reject(corrupt, OodleTestStreams.QuantumBytes + 9);
        }
        OodleTestStreams.Reject([.. stream, 0], OodleTestStreams.QuantumBytes + 9);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(127)]
    public void Unsupported_decoders_fail_clearly(int decoder) =>
        OodleTestStreams.Reject([0xCC, (byte)decoder, 42], 1);

    [Fact]
    public void Checksums_and_empty_output_residue_are_rejected()
    {
        OodleTestStreams.Reject([0xCC, 0x86, 42], 1);
        OodleTestStreams.Reject([0x8C, 0x86, 0, 0, 0, 0, 0, 0, 42], 1);
        OodleTestStreams.Reject([0], 0);
        OodleTestStreams.Reject([], -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => OodleCodec.Decompress([], 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => OodleCodec.EncodeStored([], OodleCodec.MaximumBytes + 1));
    }
}
