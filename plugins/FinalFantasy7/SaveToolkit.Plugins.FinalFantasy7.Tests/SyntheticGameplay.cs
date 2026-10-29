using System.Buffers.Binary;
using System.Text;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

/// <summary>
/// Artificial framing from the README's record-group table, independent of Ff7Schema.Shape/Parse.
/// Bodies are deliberately meaningless; this fixture proves framing, not real gameplay semantics.
/// </summary>
internal sealed record SyntheticGameplay(byte[] Bytes, IReadOnlyList<SyntheticRecord> Records)
{
    public const int EnvelopeLength = 16;
    public const int BodyOffset = 40;

    public static SyntheticGameplay Create(string title)
    {
        var remake = title == "PPSA01911";
        var bytes = new byte[remake ? 9_434_752 : 23_381_104];
        var records = new List<SyntheticRecord>();
        var offset = 0;
        if (remake)
        {
            Add(0, "RESD", 7, 0x50000, 1);
            Add(1, "LOSD", 13, 0x66100, 18);
            Add(2, "RESD", 7, 0x50000, 1);
            Add(3, "LOSD", 13, 0x66100, 3);
        }
        else
        {
            Add(0, "RESD", 14, 0xE4320, 1);
            Add(1, "LOSD", 27, 0x14F50, 80);
            Add(2, "RESD", 14, 0xE4320, 1);
            Add(3, "LOSD", 27, 0x14F50, 80);
            Add(11, "MLSD", 2, 0x69030, 8);
            Add(10, "MLSD", 2, 0x69030, 8);
            Add(14, "NVSD", 1, 0x2440, 48);
            Add(15, "NVSD", 1, 0x2440, 48);
        }

        if (offset != bytes.Length - EnvelopeLength)
            throw new InvalidOperationException("Synthetic record groups must end exactly at the tail.");
        Write(offset, 0x00010004);
        Write(offset + 8, remake ? 0U : EnvelopeLength);
        Write(offset + 12, remake ? 0U : 1U);
        records.Add(new(offset, EnvelopeLength, 4, "TAIL", 0, false));
        return new(bytes, records);

        void Add(byte kind, string tag, uint version, int contentLength, int count)
        {
            for (var i = 0; i < count; i++)
            {
                // Include a vacant allocation with a nonzero body that must also be retained.
                var empty = records.Count == 2;
                var length = contentLength + EnvelopeLength;
                Write(offset, (uint)(kind | 0x00010000));
                Write(offset + 4, (uint)(offset + length));
                Write(offset + 8, remake ? 0U : (uint)contentLength);
                Write(offset + 12, remake ? 0U : records.Count % 2 == 0 ? 1U : 3U);
                if (!empty)
                {
                    Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(tag, 4)), bytes.AsSpan(offset + EnvelopeLength));
                    Write(offset + 32, version);
                    Write(offset + 36, (uint)contentLength);
                }
                bytes[offset + BodyOffset] = 0xA5;
                bytes[offset + length - 1] = 0x5A;
                records.Add(new(offset, length, kind, empty ? "Empty" : tag, empty ? 0 : version, empty));
                offset += length;
            }
        }

        void Write(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
    }
}

internal sealed record SyntheticRecord(int Offset, int Length, byte Kind, string Tag, uint Version, bool Empty);
