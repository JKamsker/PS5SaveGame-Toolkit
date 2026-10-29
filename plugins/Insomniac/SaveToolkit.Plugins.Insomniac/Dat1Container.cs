using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed record Dat1Section(int Index, uint Tag, uint Offset, uint Size)
{
    public uint End => checked(Offset + Size);
}

internal sealed class Dat1Container
{
    internal const int HeaderSize = 16;
    private const uint Magic = 0x4441_5431;
    private const uint MaxFileSize = 64 * 1024 * 1024;
    private const int MaxRecords = 4096;

    private Dat1Container(byte[] bytes, uint word, uint totalSize, ushort sectionCount, ushort auxiliaryCount,
        byte[] auxiliary, IReadOnlyList<Dat1Section> sections, int descriptorEnd, int dataStart)
    {
        Bytes = bytes;
        Word = word;
        TotalSize = totalSize;
        SectionCount = sectionCount;
        AuxiliaryCount = auxiliaryCount;
        Auxiliary = auxiliary;
        Sections = sections;
        DescriptorEnd = descriptorEnd;
        DataStart = dataStart;
    }

    internal byte[] Bytes { get; }
    internal uint Word { get; }
    internal uint TotalSize { get; }
    internal ushort SectionCount { get; }
    internal ushort AuxiliaryCount { get; }
    internal byte[] Auxiliary { get; }
    internal IReadOnlyList<Dat1Section> Sections { get; }
    internal int DescriptorEnd { get; }
    internal int DataStart { get; }
    internal int PrefixLength => DataStart - DescriptorEnd;

    internal static void ValidateFileLength(long length)
    {
        if (length < HeaderSize || length > MaxFileSize)
            throw new SaveFormatException("DAT1 file size is outside the supported range.");
    }

    internal static bool LooksLike(ReadOnlySpan<byte> header, long fileLength)
    {
        if (header.Length < HeaderSize || fileLength < HeaderSize || fileLength > MaxFileSize)
        {
            return false;
        }

        var total = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        var sections = BinaryPrimitives.ReadUInt16LittleEndian(header[12..]);
        var auxiliary = BinaryPrimitives.ReadUInt16LittleEndian(header[14..]);
        return BinaryPrimitives.ReadUInt32LittleEndian(header) == Magic &&
            BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) == 8 &&
            total == fileLength && sections <= MaxRecords && auxiliary <= MaxRecords;
    }

    internal static Dat1Container Parse(byte[] source)
    {
        ValidateFileLength(source.Length);

        var bytes = source.ToArray();
        var span = bytes.AsSpan();
        var magic = ReadUInt32(span, 0);
        var word = ReadUInt32(span, 4);
        var totalSize = ReadUInt32(span, 8);
        var sectionCount = ReadUInt16(span, 12);
        var auxiliaryCount = ReadUInt16(span, 14);
        if (magic != Magic)
        {
            throw new SaveFormatException("DAT1 magic is missing.");
        }

        if (word != 8)
        {
            throw new SaveFormatException($"Unsupported DAT1 header word 0x{word:X8}; expected 8.");
        }

        if (totalSize != bytes.Length)
        {
            throw new SaveFormatException($"DAT1 total size ({totalSize}) does not match the file length ({bytes.Length}).");
        }

        if (sectionCount > MaxRecords || auxiliaryCount > MaxRecords)
        {
            throw new SaveFormatException("DAT1 descriptor count is unreasonably large.");
        }

        var descriptorEnd = checked(HeaderSize + sectionCount * 12 + auxiliaryCount * 8);
        if (descriptorEnd > bytes.Length)
        {
            throw new SaveFormatException("DAT1 descriptor table is truncated.");
        }

        var sections = new List<Dat1Section>(sectionCount);
        var position = HeaderSize;
        for (var index = 0; index < sectionCount; index++)
        {
            var tag = ReadUInt32(span, position);
            var offset = ReadUInt32(span, position + 4);
            var size = ReadUInt32(span, position + 8);
            if (offset < descriptorEnd || (ulong)offset + size > totalSize)
            {
                throw new SaveFormatException($"DAT1 section {index} lies outside the file.");
            }

            sections.Add(new Dat1Section(index, tag, offset, size));
            position += 12;
        }

        var auxiliary = bytes.AsSpan(position, auxiliaryCount * 8).ToArray();
        ValidateNonOverlapping(sections);
        var dataStart = sections.Count == 0 ? descriptorEnd : checked((int)sections.Min(s => s.Offset));
        return new Dat1Container(bytes, word, totalSize, sectionCount, auxiliaryCount, auxiliary, sections, descriptorEnd, dataStart);
    }

    internal ReadOnlySpan<byte> SectionBytes(Dat1Section section) => Bytes.AsSpan(checked((int)section.Offset), checked((int)section.Size));

    private static void ValidateNonOverlapping(IReadOnlyList<Dat1Section> sections)
    {
        var ordered = sections.OrderBy(s => s.Offset).ToArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index - 1].End > ordered[index].Offset)
            {
                throw new SaveFormatException("DAT1 sections overlap.");
            }
        }
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
}
