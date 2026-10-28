using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal sealed record LinkedRecord(int Index, int Offset, int Length, uint Next, byte Kind, byte Slot,
    byte State, uint HeaderSize, uint Flags, string Tag, uint Version, uint DeclaredLength)
{
    public bool IsTail => Kind == 4;
    public bool IsEmpty => !IsTail && DeclaredLength == 0;
}

/// <summary>Walks only bounded headers. Bodies remain slices of the original buffer until requested.</summary>
internal static class LinkedRecords
{
    public const int HeaderLength = 16;
    public const int ContentOffset = 40;
    public const int NextPointerOffset = 4;
    public const int AllocationLengthOffset = 8;
    public const int FlagsOffset = 12;
    public const int VersionOffset = 32;
    public const int DeclaredLengthOffset = 36;
    private const int MaximumRecords = 512;
    private const uint TailMarker = 0x00010004;

    public static IReadOnlyList<LinkedRecord> Parse(ReadOnlySpan<byte> bytes, Ff7Schema schema, bool common)
    {
        if (bytes.Length != schema.FileLength(common) || !schema.MatchesFirstHeader(bytes, common))
        {
            throw new SaveFormatException("FFVII file header or size does not match the selected PS5 schema.");
        }

        var records = new List<LinkedRecord>(schema.RecordCount(common));
        var offset = 0;
        while (records.Count < MaximumRecords)
        {
            if (offset > bytes.Length - HeaderLength || records.Count >= schema.RecordCount(common))
            {
                throw Invalid(offset, "missing tail or too many records");
            }

            var header = bytes[offset..];
            var marker = U32(header);
            var next = U32(header[NextPointerOffset..]);
            var size = U32(header[AllocationLengthOffset..]);
            var flags = U32(header[FlagsOffset..]);
            var shape = schema.Shape(records.Count, common);
            if (header[0] != shape.Kind)
            {
                throw Invalid(offset, "unexpected record kind or premature tail");
            }

            if (header[0] == 4)
            {
                // Rebirth common has next=16, size=0; gameplay has next=0, size=16.
                var expectedNext = !schema.IsRemake && common ? HeaderLength : 0;
                var expectedSize = !schema.IsRemake && !common ? HeaderLength : 0;
                if (marker != TailMarker || next != expectedNext || size != expectedSize ||
                    flags != (schema.IsRemake ? 0U : 1U) || offset != bytes.Length - HeaderLength)
                {
                    throw Invalid(offset, "invalid terminal record");
                }

                records.Add(new(records.Count, offset, HeaderLength, next, 4, header[1], header[2], size, flags, "TAIL", 0, 0));
                return records;
            }

            // Strictly increasing absolute offsets reject cycles, overlap, wraparound and backward links.
            if (next > bytes.Length - HeaderLength || next <= offset || next - offset != shape.Length + HeaderLength)
            {
                throw Invalid(offset, "next record pointer is outside the supported record boundary");
            }

            var length = (int)next - offset;
            if (length < ContentOffset || header[3] != 0 || header[2] > 1 ||
                size != (schema.IsRemake ? 0U : (uint)shape.Length) ||
                (schema.IsRemake ? flags != 0 : flags is not (1 or 3)))
            {
                throw Invalid(offset, "invalid record envelope");
            }

            var tagBytes = header.Slice(HeaderLength, 16);
            var version = U32(header[VersionOffset..]);
            var declared = U32(header[DeclaredLengthOffset..]);
            var empty = tagBytes.IndexOfAnyExcept((byte)0) < 0;
            if (empty ? version != 0 || declared != 0 :
                declared != shape.Length || !TagMatches(tagBytes, shape.Tag) ||
                (shape.Tag is "MLSD" or "NVSD" ? (version & byte.MaxValue) != shape.Version : version != shape.Version))
            {
                throw Invalid(offset, "invalid repeated tag, version or content length");
            }

            if (records.Count == 0 && empty)
            {
                throw Invalid(offset, "first record is empty");
            }

            records.Add(new(records.Count, offset, length, next, header[0], header[1], header[2], size,
                flags, empty ? "Empty" : shape.Tag, version, declared));
            offset = (int)next;
        }

        throw Invalid(offset, "record limit exceeded");
    }

    private static bool TagMatches(ReadOnlySpan<byte> bytes, string tag)
    {
        Span<byte> expected = stackalloc byte[4];
        Encoding.ASCII.GetBytes(tag, expected);
        for (var i = 0; i < bytes.Length; i += expected.Length)
        {
            if (!bytes.Slice(i, expected.Length).SequenceEqual(expected))
            {
                return false;
            }
        }

        return true;
    }

    private static uint U32(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    private static SaveFormatException Invalid(int offset, string reason) => new($"FFVII record at 0x{offset:X}: {reason}.");
}
