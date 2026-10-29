using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class PackedDataCorruptionTests
{
    [Theory]
    [InlineData(new byte[] { 0x40 }, "CSAV range at 0x1 exceeds its containing data.", 2)]
    [InlineData(new byte[] { 0x40, 0x80 }, "CSAV range at 0x2 exceeds its containing data.", 3)]
    [InlineData(new byte[] { 0x40, 0x80, 0x80 }, "CSAV range at 0x3 exceeds its containing data.", 4)]
    [InlineData(new byte[] { 0x40, 0x80, 0x80, 0x80, 0x80 }, "CSAV packed integer is too long.", 5)]
    [InlineData(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF, 0x7F }, "CSAV packed integer exceeds the supported range.", 5)]
    public void TruncatedOverlongAndOverflowedPackedIntegersUseFormatErrors(byte[] bytes, string message, int finalPosition)
    {
        var position = 0;
        Assert.Equal(message, Assert.Throws<SaveFormatException>(() => BinaryData.PackedInt(bytes, ref position)).Message);
        Assert.Equal(finalPosition, position);
    }

    [Theory]
    [InlineData(new byte[] { 1 }, "Unsupported CSAV node name encoding or length.")]
    [InlineData(new byte[] { 0x82, (byte)'a' }, "CSAV range at 0x1 exceeds its containing data.")]
    [InlineData(new byte[] { 0x81, 0x1F }, "CSAV node name is not printable ASCII.")]
    [InlineData(new byte[] { 0x81, 0xFF }, "CSAV node name is not printable ASCII.")]
    [InlineData(new byte[] { 0xC1, 0x10 }, "Unsupported CSAV node name encoding or length.")]
    public void InvalidNodeNamesUseFormatErrors(byte[] bytes, string message)
    {
        var position = 0;
        Assert.Equal(message, Assert.Throws<SaveFormatException>(() => BinaryData.PackedName(bytes, ref position)).Message);
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
