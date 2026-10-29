using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class ContainerDecodingTests
{
    private const int FirstBlockCapacity = 8 * SyntheticDsss.CapacityUnit;
    private const int SecondBlockOffset = DsssContainer.HeaderSize + DsssContainer.MetadataSize + FirstBlockCapacity;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void BlockHashFailuresKeepExactPhysicalBlockIndex(int blockIndex)
    {
        var bytes = SyntheticDsss.Create(new byte[FirstBlockCapacity + 1]);
        var offset = blockIndex == 0 ? DsssContainer.HeaderSize : SecondBlockOffset;
        bytes[offset + DsssContainer.MetadataSize] ^= 1;
        SampleEvidenceTests.RepairOuterHash(bytes);
        Assert.Equal($"Mandarin CityHash64 mismatch in block {blockIndex}.",
            Assert.Throws<SaveFormatException>(() => new DsssContainer(bytes)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteExtentPreflightPrecedesEvenFirstBlockMetadataValidation(bool extraByte)
    {
        var original = SyntheticDsss.Create(new byte[FirstBlockCapacity + 1]);
        var bodyEnd = original.Length - SyntheticDsss.TrailerSize;
        var changedBodyEnd = extraByte ? bodyEnd + 1 : SecondBlockOffset;
        var bytes = new byte[changedBodyEnd + SyntheticDsss.TrailerSize];
        original.AsSpan(0, Math.Min(bodyEnd, changedBodyEnd)).CopyTo(bytes);
        original.AsSpan(bodyEnd).CopyTo(bytes.AsSpan(changedBodyEnd));
        bytes[DsssContainer.HeaderSize] ^= 1;
        SampleEvidenceTests.RepairOuterHash(bytes);
        Assert.Equal(extraByte ? "Mandarin block layout does not match the declared payload length." : "Truncated Mandarin block.",
            Assert.Throws<SaveFormatException>(() => new DsssContainer(bytes)).Message);
    }

    [Fact]
    public void MultiBlockEditsAndReversalPreserveUnknownMetadataPaddingHeaderAndTrailer()
    {
        var plaintext = Enumerable.Range(0, FirstBlockCapacity + 1).Select(index => (byte)(index % 251)).ToArray();
        var original = SyntheticDsss.Create(plaintext);
        original.AsSpan(12, 4).Fill(0xa5);
        original.AsSpan(DsssContainer.HeaderSize + DsssContainer.ChecksumOffset + sizeof(ulong), sizeof(ulong)).Fill(0x5a);
        var unusedOffset = SecondBlockOffset + DsssContainer.MetadataSize + 1;
        original.AsSpan(unusedOffset, original.Length - SyntheticDsss.TrailerSize - unusedOffset).Fill(0x3c);
        original.AsSpan(original.Length - SyntheticDsss.TrailerSize, 128).Fill(0xc3);
        SampleEvidenceTests.RepairOuterHash(original);
        var container = new DsssContainer(original);
        Assert.Equal(2, container.BlockCount);
        Assert.Equal(plaintext, container.Plaintext);
        Assert.Equal(original, container.Rebuild(plaintext));

        var edited = plaintext.ToArray();
        edited[FirstBlockCapacity - 1] ^= 1;
        edited[FirstBlockCapacity] ^= 1;
        var modified = container.Rebuild(edited);
        var reopened = new DsssContainer(modified);
        Assert.Equal(edited, reopened.Plaintext);
        Assert.Equal(original, reopened.Rebuild(plaintext));
    }
}
