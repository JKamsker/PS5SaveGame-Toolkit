using SaveToolkit.Codecs;
using SaveToolkit.Testing;

namespace SaveToolkit.Api.Performance.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class KrakenAllocationTests
{
    private const long SimpleDecodeAllocationLimit = 64 * 1024;
    private const int QuantumBytes = 256 * 1024;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoredAndMemsetQuantaDoNotAllocateEntropyScratch(bool memset)
    {
        var expected = Enumerable.Repeat((byte)42, memset ? 500 : 32).ToArray();
        var stream = memset ? [0x8C, 6, 7, 255, 255, 42] : OodleCodec.EncodeStored(expected);
        _ = OodleCodec.Decompress(stream, expected.Length);

        var allocated = AllocationProbe.Measure(() => OodleCodec.Decompress(stream, expected.Length), out var actual);

        Assert.Equal(expected, actual);
        allocated.AssertLessThan(SimpleDecodeAllocationLimit, $"Simple decoding allocated {allocated.MinimumBytes:N0} bytes.");
    }

    [Fact]
    public void EntropyScratchCanBeCreatedAfterAStoredQuantum()
    {
        var stored = Enumerable.Repeat((byte)17, QuantumBytes).ToArray();
        byte[] entropy = [0x8C, 6, 0, 0, 5, 0x30, 0, 0xFC, 0, 1, 42];
        byte[] stream = [.. OodleCodec.EncodeStored(stored), .. entropy];
        var expected = stored.Concat(Enumerable.Repeat((byte)42, 64)).ToArray();

        Assert.Equal(expected, OodleCodec.Decompress(stream, expected.Length));
        Assert.Throws<InvalidDataException>(() => OodleCodec.Decompress(stream.AsSpan(0, stream.Length - 1), expected.Length));
        Assert.Throws<InvalidDataException>(() => OodleCodec.Decompress([.. stream, 0], expected.Length));
    }
}
