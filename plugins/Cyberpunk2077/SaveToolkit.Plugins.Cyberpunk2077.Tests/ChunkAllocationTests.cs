using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class ChunkAllocationTests
{
    [Fact]
    public void ValidChunkAllocationGrowsWithExpandedBytes()
    {
        const int size = 64 * 1024;
        var small = Fixture(size);
        var large = Fixture(2 * size);
        var baseline = AllocationProbe.Measure(() => CsavContainer.Parse(small));
        var doubled = AllocationProbe.Measure(() => CsavContainer.Parse(large));
        doubled.AssertAtMost(3 * baseline.MinimumBytes, "Doubling valid chunk bytes must retain linear allocation growth.");
        TestContext.Current.TestOutputHelper?.WriteLine($"Valid chunks: {baseline}; doubled: {doubled}");

        static byte[] Fixture(int length) => CsavCorruptionFixture.Create(new byte[length],
            [new("x", -1, -1, 0, length)], compressed: true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MalformedBlocksAreRejectedBeforeAllocatingTheCombinedPayload(int corruptChunk)
    {
        // Two compressed literal blocks: corruption of a later chunk must also be preflighted.
        var bytes = CsavCorruptionFixture.Create(new byte[8], [new("x", -1, -1, 0, 8)], [4, 4], compressed: true);
        var descriptor = CsavCorruptionFixture.TableOffset + corruptChunk * CsavCorruptionFixture.EntrySize;
        var offset = BinaryData.Int32(bytes, descriptor);
        const int declaredSize = 32 * 1024 * 1024;
        CsavCorruptionFixture.SetWord(bytes, descriptor + 8, declaredSize);
        CsavCorruptionFixture.SetWord(bytes, offset + 4, declaredSize);

        AllocationProbe.Measure(() => Assert.Throws<SaveFormatException>(() => CsavContainer.Parse(bytes)))
            .AssertLessThan(64 * 1024, "Invalid tiny blocks must not allocate their declared expanded payload.");
    }
}
