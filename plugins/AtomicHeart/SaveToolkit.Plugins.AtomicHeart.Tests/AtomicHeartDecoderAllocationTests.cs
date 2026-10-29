using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class AtomicHeartDecoderAllocationTests(ITestOutputHelper output)
{
    private const int Warmups = 5;
    private const int Measurements = 3;
    private const int ExpectedChunks = 14;
    private const int ExpectedPlainBytes = 1_751_391;
    private const int BytesPerChunkBudget = 64 * 1024;

    [Fact]
    public void SampleBatchReusesCodecWorkspaceWithoutAllocatingPerChunkResults()
    {
        var bytes = WriteTestData.Sample().ReadAllBytes();
        var outer = SaveExtensionFile.Parse(bytes);
        var expected = SlotDataDecoder.Decode(bytes, outer);
        Assert.Equal(ExpectedChunks, outer.Chunks.Count);
        Assert.Equal(ExpectedPlainBytes, expected.Plain.Length);
        // Decode also allocates the aggregate and RecordIndex. Subtract an identical
        // plain-array copy + parser pass to isolate codec allocations on the real path.
        // File reads, framing and comparison assertions are outside both measurements.
        var decode = AllocationProbe.Measure(() => SlotDataDecoder.Decode(bytes, outer), out var decoded,
            warmupRuns: Warmups, measuredRuns: Measurements);
        Assert.Equal(expected.Plain, decoded.Plain);
        var parse = AllocationProbe.Measure(() => new RecordReader(expected.Plain.ToArray()).Read(), out var parsed,
            warmupRuns: Warmups, measuredRuns: Measurements);
        GC.KeepAlive(parsed);
        var allocated = decode.TransformSamples(sample => sample - parse.MinimumBytes);
        output.WriteLine($"14-stream codec batch, excluding aggregate/parser: {allocated}.");
        allocated.AssertInRange(0, (long)ExpectedChunks * BytesPerChunkBudget,
            $"Decode: {decode}; aggregate/parser baseline: {parse}.");
    }
}
