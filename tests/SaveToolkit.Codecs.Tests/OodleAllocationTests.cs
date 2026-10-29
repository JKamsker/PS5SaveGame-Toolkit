using SaveToolkit.Testing;

namespace SaveToolkit.Codecs.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class OodleAllocationTests
{
    private const int OutputBytes = 1_000_000;
    private const int Iterations = 8;
    private const int PerDecodeOverhead = 4096;

    [Fact]
    public void Allocating_API_only_allocates_the_result_and_small_decoder_state_after_warmup()
    {
        byte[] input = OodleCodec.EncodeStored(new byte[OutputBytes]);
        for (int i = 0; i < Iterations; i++)
            _ = OodleCodec.Decompress(input, OutputBytes);

        var allocated = AllocationProbe.Measure(() =>
        {
            for (int i = 0; i < Iterations; i++)
                _ = OodleCodec.Decompress(input, OutputBytes);
        });

        allocated.AssertInRange(0, (long)Iterations * (OutputBytes + PerDecodeOverhead));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reused_workspace_avoids_output_sized_and_entropy_scratch_allocations(bool entropy)
    {
        const int entropyOutputBytes = 131072;
        int length = entropy ? entropyOutputBytes : OutputBytes;
        byte[] input = entropy
            ? OodleTestStreams.Quantum(OodleTestStreams.Entropy(3, [42], length))
            : OodleCodec.EncodeStored(new byte[length]);
        byte[] output = new byte[length];
        using var workspace = new OodleWorkspace();
        for (int i = 0; i < Iterations; i++)
            OodleCodec.Decompress(input, output, workspace);

        var allocated = AllocationProbe.Measure(() =>
        {
            for (int i = 0; i < Iterations; i++)
                OodleCodec.Decompress(input, output, workspace);
        });

        allocated.AssertInRange(0, Iterations * PerDecodeOverhead);
        Assert.All(output, value => Assert.Equal(entropy ? (byte)42 : (byte)0, value));
    }
}
