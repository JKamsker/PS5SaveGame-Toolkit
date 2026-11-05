using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class SampleChunkAllocationTests
{
    [Fact]
    public void LastCompressedSampleChunkRejectsAnIncorrectSizeBeforePayloadAllocation()
    {
        foreach (var file in ProgressionSampleTests.Files())
        {
            var bytes = file.ReadAllBytes();
            var container = CsavContainer.Parse(bytes);
            var index = Enumerable.Range(0, container.Chunks.Count).Last(index => container.Chunks[index].Compressed);
            var chunk = container.Chunks[index];
            var descriptor = CsavCorruptionFixture.TableOffset + index * CsavCorruptionFixture.EntrySize;
            CsavCorruptionFixture.SetWord(bytes, descriptor + 8, chunk.ExpandedSize + 1);
            CsavCorruptionFixture.SetWord(bytes, chunk.Offset + 4, chunk.ExpandedSize + 1);
            AllocationProbe.Measure(() => Assert.Throws<SaveFormatException>(() => CsavContainer.Parse(bytes)))
                .AssertLessThan(64 * 1024, "A late corrupt sample block must be rejected before the combined payload allocation.");
        }
    }
}
