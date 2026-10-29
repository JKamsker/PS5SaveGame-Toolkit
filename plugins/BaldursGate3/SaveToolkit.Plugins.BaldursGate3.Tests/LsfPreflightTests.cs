using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class LsfPreflightTests
{
    private const int HeaderSize = 64;
    private const int FirstSectionSize = 4 * 1024 * 1024;

    [Theory]
    [InlineData(0u, 2, 25u)]
    [InlineData(1u, 3, 49u)]
    [InlineData(0u, 1, 9u)]
    [InlineData(0u, 1, 8u * (BinaryData.MaxRecords + 1u))]
    public void InvalidTableSizesFailAtTheMetadataBoundary(uint metadata, int pair, uint size)
    {
        var bytes = ResourceFixture.Create(metadata: metadata);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(LsfLayout.MetadataOffset + pair * LsfLayout.SizePairLength), size);
        var error = Assert.Throws<SaveFormatException>(() => LsfBinary.Read(bytes));
        Assert.Contains("table", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TruncatedLateSectionDoesNotAllocateEarlierSections()
    {
        var bytes = new byte[HeaderSize + FirstSectionSize];
        ResourceFixture.Create().AsSpan(0, HeaderSize).CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(LsfLayout.MetadataOffset), FirstSectionSize);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var error = Record.Exception(() => LsfBinary.Read(bytes));
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.IsType<SaveFormatException>(error);
        Assert.True(allocated < 100_000, $"Truncated resource allocated {allocated:N0} bytes before checking all ranges.");
    }

    [Fact]
    public void InvalidHeaderDoesNotConsumeTheSharedResourceBudget()
    {
        var budget = new ResourceBudget();
        var bytes = ResourceFixture.Create()[..^1];
        Assert.Throws<SaveFormatException>(() => LsfBinary.Read(bytes, budget));
        budget.Reserve(BinaryData.MaxBufferSize, BinaryData.MaxRecords, BinaryData.MaxRecords);
    }

    [Fact]
    public void UncompressedStoredSizeMustBeZeroOrMatchExpandedSize()
    {
        var bytes = ResourceFixture.Create();
        var expanded = BinaryData.U32(bytes, LsfLayout.MetadataOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(LsfLayout.MetadataOffset + sizeof(uint)), expanded);
        Assert.Equal(2, LsfReader.Read(bytes).Nodes.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(LsfLayout.MetadataOffset + sizeof(uint)), expanded - 1);
        var error = Assert.Throws<SaveFormatException>(() => LsfBinary.Read(bytes));
        Assert.Contains("stored size", error.Message, StringComparison.Ordinal);
    }
}
