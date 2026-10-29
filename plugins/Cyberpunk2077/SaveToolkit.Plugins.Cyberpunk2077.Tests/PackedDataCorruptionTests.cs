using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class PackedDataCorruptionTests
{
    [Theory]
    [InlineData(new byte[] { 0x40 })]
    [InlineData(new byte[] { 0x40, 0x80 })]
    [InlineData(new byte[] { 0x40, 0x80, 0x80 })]
    [InlineData(new byte[] { 0x40, 0x80, 0x80, 0x80, 0x80 })]
    [InlineData(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF, 0x7F })]
    public void TruncatedOverlongAndOverflowedPackedIntegersUseFormatErrors(byte[] bytes)
    {
        var position = 0;
        Assert.Throws<SaveFormatException>(() => BinaryData.PackedInt(bytes, ref position));
    }

    [Theory]
    [InlineData(new byte[] { 1 })] // Unsupported wide-name encoding.
    [InlineData(new byte[] { 0x82, (byte)'a' })] // Truncated ASCII name.
    [InlineData(new byte[] { 0x81, 0x1F })] // Nonprintable byte.
    [InlineData(new byte[] { 0x81, 0xFF })]
    [InlineData(new byte[] { 0xC1, 0x10 })] // 1,025 ASCII bytes, above the cap.
    public void InvalidNodeNamesUseFormatErrors(byte[] bytes)
    {
        var position = 0;
        Assert.Throws<SaveFormatException>(() => BinaryData.PackedName(bytes, ref position));
    }

    [Fact]
    public void MaximumNameAndPackedMagnitudeAreAcceptedWithoutLosingBytes()
    {
        var position = 0;
        byte[] maximum = [0x7F, 0xFF, 0xFF, 0xFF, 0x0F];
        Assert.Equal(int.MaxValue, BinaryData.PackedInt(maximum, ref position));
        maximum[0] |= 0x80;
        position = 0;
        Assert.Equal(-int.MaxValue, BinaryData.PackedInt(maximum, ref position));
        var name = new string('x', 1024);
        byte[] payload = [0, 0, 0, 0];
        var bytes = CsavCorruptionFixture.Create(payload, [new(name, -1, -1, 0, 4)]);
        var container = CsavContainer.Parse(bytes);
        Assert.Equal(name, Assert.Single(CsavTree.Parse(container).Nodes).Name);
        Assert.Equal(bytes, container.Rebuild(container.Payload));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0, 0, 0, 0, 0x81 })] // Negative count.
    [InlineData(new byte[] { 0, 0, 0, 0, 1 })] // Missing hash/value pair.
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 1 })] // Unexplained suffix.
    [InlineData(new byte[] { 0, 0, 0, 0, 0x7F, 0xFF, 0xFF, 0xFF, 0x0F })]
    public void InvalidFactCountsAreRejectedBeforeAllocatingEntries(byte[] bytes) =>
        Assert.Throws<SaveFormatException>(() => FactsReader.Read(bytes));
}
