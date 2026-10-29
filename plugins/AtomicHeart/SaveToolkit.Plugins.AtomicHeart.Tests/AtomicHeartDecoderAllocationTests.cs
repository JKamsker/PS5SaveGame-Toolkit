namespace SaveToolkit.Plugins.AtomicHeart.Tests;

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
        for (var i = 0; i < Warmups; i++)
        {
            _ = DecodeAllocation(bytes, outer, expected.Plain);
            _ = ParseAllocation(expected.Plain);
        }

        // Decode also allocates the aggregate and RecordIndex. Subtract an identical
        // plain-array copy + parser pass to isolate codec allocations on the real path.
        // File reads, framing and comparison assertions are outside both measurements.
        var allocations = new long[Measurements];
        for (var i = 0; i < Measurements; i++)
            allocations[i] = DecodeAllocation(bytes, outer, expected.Plain) - ParseAllocation(expected.Plain);
        Array.Sort(allocations);
        var allocated = allocations[Measurements / 2];
        output.WriteLine($"14-stream codec batch, excluding aggregate/parser: {allocated:N0} allocated bytes.");
        Assert.InRange(allocated, 0, (long)ExpectedChunks * BytesPerChunkBudget);
    }

    private static long DecodeAllocation(byte[] bytes, SaveExtensionFile outer, byte[] expected)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var decoded = SlotDataDecoder.Decode(bytes, outer);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(expected, decoded.Plain);
        return allocated;
    }

    private static long ParseAllocation(byte[] plain)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var parsed = new RecordReader(plain.ToArray()).Read();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(parsed);
        return allocated;
    }
}
