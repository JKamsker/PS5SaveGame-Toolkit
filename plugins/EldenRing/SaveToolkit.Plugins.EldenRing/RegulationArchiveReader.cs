using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.EldenRing;

internal sealed record RegulationTable(string Name, int Size, int Rows, ushort DataVersion, byte FormatVersion);
internal sealed record RegulationArchive(string BinderVersion, int DecodedSize, IReadOnlyList<RegulationTable> Tables);

// Narrow, read-only directory/header port from ClayAmore 014107f0 util/bnd4.rs and params.rs.
// No PARAM field schemas, unsafe casts, PARAM names databases or SoulsFormats code are used.
internal static class RegulationArchiveReader
{
    private const int HeaderSize = 0x40;
    private const int EntrySize = 0x24;
    private const int MaximumEntries = 4096;
    private const int MaximumNameBytes = 2048;
    private const int ParamHeaderSize = 0x40;
    private const int ParamRowSize = 0x18;
    private static readonly UnicodeEncoding StrictUnicode = new(false, false, true);

    public static RegulationArchive Read(ReadOnlySpan<byte> bytes)
    {
        Require(bytes.Length <= RegulationDecoder.MaximumDecodedSize && bytes.Length >= HeaderSize && bytes[..4].SequenceEqual("BND4"u8), "Missing or oversized regulation BND4 header.");
        Require(bytes[9] == 0 && bytes[10] == 1 && bytes[0x30] == 1 && bytes[0x31] == 0x74 && bytes[0x32] == 4,
            "Unsupported BND4 byte order, flags or encoding.");
        Require(U64(bytes, 0x10) == HeaderSize && U64(bytes, 0x20) == EntrySize, "Unsupported BND4 directory layout.");
        var count = U32(bytes, 0x0C);
        Require(count <= MaximumEntries && count <= (bytes.Length - HeaderSize) / EntrySize, "BND4 entry count exceeds its directory.");
        var headerEnd = U64(bytes, 0x28);
        var hashOffset = U64(bytes, 0x38);
        Require(headerEnd >= HeaderSize + count * EntrySize && headerEnd <= (ulong)bytes.Length
            && hashOffset >= HeaderSize + count * EntrySize && hashOffset < headerEnd, "Invalid BND4 metadata boundaries.");
        var tables = new List<RegulationTable>();
        var extents = new List<(int Offset, int Length)>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<uint>();
        for (var index = 0; index < count; index++)
        {
            var entry = HeaderSize + index * EntrySize;
            Require(bytes[entry] == 0x40 && U32(bytes, entry + 4) == uint.MaxValue, "Unsupported compressed or flagged BND4 entry.");
            var length = U64(bytes, entry + 8);
            Require(length == U64(bytes, entry + 16) && length > 0, "BND4 entry sizes disagree.");
            // Sample encoding 0x74 means bit-reversed format 0x2E: 32-bit offset + id.
            // The permissive upstream's general flag handling is not assumed correct for this PS5 archive.
            var offset = U32(bytes, entry + 24);
            Require(ids.Add(U32(bytes, entry + 28)), "Duplicate BND4 entry id.");
            Require(offset >= headerEnd && offset <= (ulong)bytes.Length && length <= (ulong)bytes.Length - offset,
                "BND4 payload is outside the decoded archive.");
            var nameOffset = U32(bytes, entry + 32);
            Require(nameOffset >= HeaderSize + count * EntrySize && nameOffset < headerEnd, "BND4 name is outside its metadata.");
            var name = Unicode(bytes, (int)nameOffset, (int)headerEnd);
            Require(names.Add(name) && name.EndsWith(".param", StringComparison.Ordinal), "Duplicate or non-PARAM regulation entry.");
            var fileName = name[(name.LastIndexOf('\\') + 1)..];
            tables.Add(Param(bytes.Slice((int)offset, (int)length), fileName));
            extents.Add(((int)offset, (int)length));
        }
        var sorted = extents.OrderBy(range => range.Offset).ToArray();
        for (var index = 1; index < sorted.Length; index++)
            Require(sorted[index - 1].Offset + sorted[index - 1].Length <= sorted[index].Offset, "BND4 payloads overlap.");
        return new RegulationArchive(Encoding.ASCII.GetString(bytes.Slice(0x18, 8)).TrimEnd('\0'), bytes.Length, tables);
    }

    private static RegulationTable Param(ReadOnlySpan<byte> bytes, string name)
    {
        Require(bytes.Length >= ParamHeaderSize && bytes[0x2C] == 0 && bytes[0x2D] == 0x85 && bytes[0x2E] == 7 && bytes[0x2F] == 0,
            "Unsupported PARAM directory schema (requires sampled little-endian long offsets).");
        // With flag 0x80 the 64-bit type offset is authoritative. The legacy string word
        // can exceed the file (three sampled Cutscene tables); it is not a data bound.
        var typeOffset = U64(bytes, 0x10);
        var rows = BinaryPrimitives.ReadUInt16LittleEndian(bytes[0x0A..]);
        var directoryEnd = ParamHeaderSize + rows * ParamRowSize;
        if (directoryEnd > bytes.Length || typeOffset < (ulong)directoryEnd || typeOffset >= (ulong)bytes.Length)
            throw new SaveFormatException($"{name}: PARAM strings or row count exceed the table.");
        // Validate the type tag's termination; never infer a field schema from an arbitrary file name.
        var typeStart = (int)typeOffset;
        var typeEnd = bytes.Slice(typeStart, Math.Min(MaximumNameBytes, bytes.Length - typeStart)).IndexOf((byte)0);
        Require(typeEnd > 0, "PARAM type tag is unterminated.");
        for (var row = 0; row < rows; row++)
        {
            var position = ParamHeaderSize + row * ParamRowSize;
            var dataOffset = U64(bytes, position + 8);
            var nameOffset = U64(bytes, position + 16);
            // Duplicate numeric row ids occur in the real snapshot; row identity is not id alone.
            // Keep contextual diagnostics on the failure path: large PARAM tables have
            // thousands of valid rows and must not allocate two discarded messages per row.
            if (dataOffset < (ulong)directoryEnd || dataOffset >= typeOffset)
                throw new SaveFormatException($"{name}: PARAM has invalid row data offsets.");
            if (nameOffset != 0 && (nameOffset < typeOffset || nameOffset >= (ulong)bytes.Length))
                throw new SaveFormatException($"{name}: PARAM row name is outside its string region.");
        }
        return new RegulationTable(name, bytes.Length, rows, BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]), bytes[0x2F]);
    }

    private static string Unicode(ReadOnlySpan<byte> bytes, int offset, int end)
    {
        var limit = Math.Min(end - offset, MaximumNameBytes);
        for (var length = 0; length + 1 < limit; length += sizeof(ushort))
        {
            if (bytes[offset + length] != 0 || bytes[offset + length + 1] != 0) continue;
            try { return StrictUnicode.GetString(bytes.Slice(offset, length)); }
            catch (DecoderFallbackException exception) { throw new SaveFormatException("Invalid BND4 UTF-16 name.", exception); }
        }
        throw new SaveFormatException("BND4 name is unterminated or too long.");
    }

    private static uint U32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static ulong U64(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new SaveFormatException(message);
    }
}
