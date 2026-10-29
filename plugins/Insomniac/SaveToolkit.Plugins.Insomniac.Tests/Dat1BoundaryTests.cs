using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class Dat1BoundaryTests
{
    private const int HeaderSize = 16;
    private const int SectionDescriptorSize = 12;
    private const int AuxiliaryRecordSize = 8;
    private const int AuxiliaryOffset = HeaderSize + SectionDescriptorSize;
    private const int DescriptorEnd = AuxiliaryOffset + AuxiliaryRecordSize;
    private const int DataStart = 48;
    private const int DataSize = 4;
    private const int TrailerSize = 7;
    private const int MaxRecords = 4096;
    private const uint OpaqueTag = 0x1234_5678;
    private static readonly byte[] Auxiliary = [0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10];

    [Theory]
    [InlineData("short-header", "file size is outside")]
    [InlineData("section-count", "descriptor count is unreasonably large")]
    [InlineData("auxiliary-count", "descriptor count is unreasonably large")]
    [InlineData("section-table", "descriptor table is truncated")]
    [InlineData("auxiliary-table", "descriptor table is truncated")]
    [InlineData("section-in-header", "section 0 lies outside")]
    [InlineData("section-in-auxiliary", "section 0 lies outside")]
    [InlineData("section-past-end", "section 0 lies outside")]
    [InlineData("section-size-past-end", "section 0 lies outside")]
    [InlineData("section-extent-overflow", "section 0 lies outside")]
    public void InvalidMetadataAndSectionExtentsAreRejectedSpecifically(string corruption, string message)
    {
        var bytes = Fixture();
        switch (corruption)
        {
            case "short-header": bytes = bytes[..(HeaderSize - 1)]; break;
            case "section-count": WriteCount(12, MaxRecords + 1); break;
            case "auxiliary-count": WriteCount(14, MaxRecords + 1); break;
            case "section-table": WriteCount(12, 4); break;
            case "auxiliary-table": WriteCount(14, 4); break;
            case "section-in-header": Write(20, HeaderSize - 1); break;
            case "section-in-auxiliary": Write(20, DescriptorEnd - 1); break;
            case "section-past-end": Write(20, (uint)bytes.Length + 1); break;
            case "section-size-past-end": Write(24, (uint)(bytes.Length - DataStart + 1)); break;
            case "section-extent-overflow": Write(20, uint.MaxValue - 1); Write(24, 4); break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        var direct = Assert.Throws<SaveFormatException>(() => Dat1Container.Parse(bytes));
        Assert.Contains(message, direct.Message);
        var error = Assert.Throws<SaveFormatException>(() =>
            new InsomniacPlugin().Open(MemorySaveSet.SingleFile("test.save", bytes)));
        Assert.Contains(message, error.Message);

        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        void WriteCount(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
    }

    [Fact]
    public void AuxiliaryRecordsPrefixAndTrailingBytesSurviveWithoutInterpretation()
    {
        var bytes = Fixture();
        var container = Dat1Container.Parse(bytes);
        Assert.Equal(1, container.AuxiliaryCount);
        Assert.Equal(Auxiliary, container.Auxiliary);
        Assert.Equal(DescriptorEnd, container.DescriptorEnd);
        Assert.Equal(DataStart, container.DataStart);
        Assert.Equal(DataStart - DescriptorEnd, container.PrefixLength);
        var section = Assert.Single(container.Sections);
        Assert.Equal(OpaqueTag, section.Tag);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, container.SectionBytes(section).ToArray());
        var file = MemorySaveSet.SingleFile("test.save", bytes);
        using var document = new InsomniacPlugin().Open(file);
        _ = document.BuildView();
        var output = Assert.Single(document.Serialize());
        Assert.Equal(file.RelativePath, output.RelativePath);
        Assert.Equal(bytes, output.Content.ToArray());
        // Parsing must own its bytes; caller mutations must not rewrite imported state.
        bytes[AuxiliaryOffset] ^= 1;
        Assert.Equal(Auxiliary, container.Auxiliary);
        Assert.Equal(Auxiliary, container.Bytes.AsSpan(AuxiliaryOffset, AuxiliaryRecordSize).ToArray());
        Assert.Equal(output.Content.ToArray(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, MaxRecords)]
    [InlineData(MaxRecords, 0)]
    public void EmptySectionsAndMaximumDescriptorCountsAreAccepted(int sections, int auxiliary)
    {
        var length = HeaderSize + sections * SectionDescriptorSize + auxiliary * AuxiliaryRecordSize;
        var bytes = new byte[length];
        Header(bytes, (ushort)sections, (ushort)auxiliary);
        for (var index = 0; index < sections; index++)
        {
            var offset = HeaderSize + index * SectionDescriptorSize;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), (uint)index);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4), (uint)length);
        }
        var container = Dat1Container.Parse(bytes);
        Assert.Equal(sections, container.Sections.Count);
        Assert.Equal(auxiliary * AuxiliaryRecordSize, container.Auxiliary.Length);
        Assert.Equal(length, container.DescriptorEnd);
        Assert.Equal(length, container.DataStart);
        Assert.True(Dat1Container.LooksLike(bytes.AsSpan(0, HeaderSize), bytes.Length));
        Assert.Equal(bytes, container.Bytes);
        using var document = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("test.save", bytes));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static byte[] Fixture()
    {
        var bytes = new byte[DataStart + DataSize + TrailerSize];
        bytes.AsSpan().Fill(0xA5);
        Header(bytes, 1, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(HeaderSize), OpaqueTag);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(HeaderSize + 4), DataStart);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(HeaderSize + 8), DataSize);
        Auxiliary.CopyTo(bytes, AuxiliaryOffset);
        new byte[] { 1, 2, 3, 4 }.CopyTo(bytes, DataStart);
        return bytes;
    }

    private static void Header(byte[] bytes, ushort sections, ushort auxiliary)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x4441_5431);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), sections);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), auxiliary);
    }
}
