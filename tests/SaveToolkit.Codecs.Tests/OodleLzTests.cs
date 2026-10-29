namespace SaveToolkit.Codecs.Tests;

public sealed class OodleLzTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Raw_and_delta_match_runs_decode_known_seed(int mode)
    {
        // Eight seed literals followed by a 56-byte overlapping match at distance eight.
        byte[] stream = Build(mode, 0x3C, 39);
        byte[] expected = Enumerable.Range(0, 8).SelectMany(_ => "abcdefgh"u8.ToArray()).ToArray();
        Assert.Equal(expected, OodleCodec.Decompress(stream, 64));
        for (int length = 0; length < stream.Length; length++)
            OodleTestStreams.Reject(stream.AsSpan(0, length).ToArray(), 64);
        OodleTestStreams.Reject([.. stream, 0], 64);
    }

    [Theory]
    [InlineData(0, 0x3C, 40)] // Match exceeds the remaining output by one.
    [InlineData(1, 0x3C, 40)]
    [InlineData(0, 0x3C, 255)] // Extended length is absent from the offset bitstream.
    [InlineData(1, 0x3C, 255)]
    [InlineData(0, 0xFC, 39)] // Select a new offset from an empty offset table.
    [InlineData(1, 0xFC, 39)]
    [InlineData(0, 0x3F, 39)] // Request literals from an empty literal stream.
    [InlineData(1, 0x3F, 39)]
    [InlineData(2, 0x3C, 39)] // Unsupported literal mode.
    public void Malformed_match_commands_reject_cleanly(int mode, byte command, byte length) =>
        OodleTestStreams.Reject(Build(mode, command, length), 64);

    private static byte[] Build(int mode, byte command, byte length)
    {
        byte[] table = [.. "abcdefgh"u8.ToArray(),
            0, 0, 0, // Empty literal stream.
            0, 0, 1, command,
            0, 0, 0, // Empty offset stream: use the initial recent distance of eight.
            0, 0, 1, length,
            0x80]; // Zero extended lengths, encoded from the backward end.
        int header = 0x800000 | (mode << 19) | table.Length;
        return OodleTestStreams.Quantum([(byte)(header >> 16), (byte)(header >> 8), (byte)header, .. table]);
    }
}
