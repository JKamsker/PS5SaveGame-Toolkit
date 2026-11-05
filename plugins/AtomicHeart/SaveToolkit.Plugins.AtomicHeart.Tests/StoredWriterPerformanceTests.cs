using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class StoredWriterPerformanceTests
{
    [Fact]
    public void DoublingChangedFramesKeepsWriterAllocationLinear()
    {
        var small = Measure(32);
        var large = Measure(64);
        large.AssertLessThan(small.MinimumBytes * 5 / 2,
            "Doubling changed frame bytes must not introduce quadratic aggregate copies.");
    }

    private static AllocationProbeResult Measure(int chunks)
    {
        // The frame writer is independent of the inner grammar. Use authored opaque frames;
        // production serialization separately reparses its edited inner records.
        var frame = new byte[SaveExtensionFile.ChunkSize];
        var original = SlotFixture.Create(chunks: chunks, plain: frame);
        var file = SaveExtensionFile.Parse(original);
        var baseline = new byte[chunks * frame.Length];
        var changed = new byte[baseline.Length];
        Array.Fill(changed, (byte)1);
        var allocated = AllocationProbe.Measure(() => SlotDataWriter.Write(original, file, baseline, changed), out var output);
        var parsed = SaveExtensionFile.Parse(output);
        Assert.Equal(chunks, parsed.Chunks.Count);
        foreach (var chunk in parsed.Chunks)
        {
            Assert.Equal(frame.Length + 2, chunk.CompressedBytes);
            var payload = output.AsSpan(chunk.Offset + SlotDataDecoder.FrameHeaderBytes, chunk.CompressedBytes);
            Assert.Equal(0xCC, payload[0]);
            Assert.Equal(6, payload[1]);
            Assert.True(payload[2..].SequenceEqual(changed.AsSpan(0, frame.Length)));
        }
        TestContext.Current.TestOutputHelper!.WriteLine($"{chunks} changed frames: {allocated.MinimumBytes} B allocated");
        return allocated;
    }
}
