using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherContainerRobustnessTests
{
    [Theory]
    [InlineData(8, -1)]
    [InlineData(8, 0)]
    [InlineData(8, 4097)]
    [InlineData(12, -1)]
    [InlineData(12, 39)]
    [InlineData(12, int.MaxValue)]
    [InlineData(16, -1)]
    [InlineData(16, 0)]
    [InlineData(16, int.MaxValue)]
    [InlineData(20, -1)]
    [InlineData(20, 0)]
    [InlineData(20, 67108865)]
    [InlineData(24, -1)]
    [InlineData(24, int.MaxValue)]
    [InlineData(28, -1)]
    [InlineData(28, int.MaxValue)]
    [InlineData(32, -1)]
    [InlineData(32, 67108864)]
    [InlineData(36, -1)]
    [InlineData(36, int.MaxValue)]
    public void EveryChunkDescriptorFieldRejectsInvalidBounds(int offset, int value)
    {
        var bytes = SyntheticSave.Create();
        RobustnessSave.WriteInt(bytes, offset, value);
        var error = Assert.Throws<SaveFormatException>(() => WitcherContainer.Parse(bytes));
        Assert.Contains("Witcher", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RawChunkAndBothEndConventionsPreserveOpaqueTrailer()
    {
        byte[] payload = [1, 2, 3, 4];
        var bytes = RobustnessSave.StoredChunk(payload, payload.Length);
        Assert.Equal(payload, WitcherContainer.Parse(bytes).Payload);
        RobustnessSave.WriteInt(bytes, 24, bytes.Length);
        bytes = [.. bytes, 0xAA, 0x55];
        var container = WitcherContainer.Parse(bytes);
        Assert.Equal(payload, container.Payload);
        Assert.Equal(bytes, container.Original);
    }

    [Fact]
    public void EveryPhysicalPrefixBeforeCompleteChunkDataIsRejected()
    {
        var bytes = SyntheticSave.Create();
        var container = WitcherContainer.Parse(bytes);
        var end = container.Chunks[^1].Offset + container.Chunks[^1].StoredSize;
        for (var length = 0; length < end; length++)
        {
            Assert.Throws<SaveFormatException>(() => RobustnessSave.OpenContainer(bytes[..length]));
        }
        // The optional two-byte opaque trailer can be absent, partial, or complete.
        for (var length = end; length <= bytes.Length; length++)
        {
            using var document = RobustnessSave.OpenContainer(bytes[..length]);
            Assert.Equal(bytes[..length], Assert.Single(document.Serialize()).Content.ToArray());
        }
    }
}
