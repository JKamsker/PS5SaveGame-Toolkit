using System.Buffers.Binary;

namespace SaveToolkit.Plugins.Gearbox.Tests;

/// <summary>Independent bounded framing/wire probe. Only gameplay tags are interpreted; no identities.</summary>
internal static class IndependentSaveProbe
{
    private const int MaximumBytes = 32 * 1024 * 1024;
    private const int MaximumRecords = 250_000;
    // Public PS4 mask facts; Rick/Gibbed zlib notice retained in Notices/Gibbed-BogoCrypt.txt.
    private const string CharacterPrefix = "D17BBF754CC180303792BDD0183E4A5F43A246A0EDDB2D9F565F8B3D6E73E6B8";
    private const string CharacterXor = "FBFDFD513A5CDB20BB5EC7AF666FB69A9A52670F195DD3841519C94A7967DA6D";
    private const string ProfilePrefix = "AD1E604E429EA933B2F501E1024D0875B1AD1A3DA1036B1A17E6EC0F608DB4F9";
    private const string ProfileXor = "BA0E861D58E1922130D6CBF0D082D5583612E1F6394488EA4EFB047407953AA2";

    private static byte[] Decode(byte[] file, bool profile)
    {
        if (file.Length > MaximumBytes || file.Length < 26 || !file.AsSpan(0, 4).SequenceEqual("GVAS"u8))
            throw new InvalidDataException("Probe framing rejected.");
        var cursor = 22;
        SkipString(file, ref cursor);
        _ = Int32(file, ref cursor);
        var customCount = Int32(file, ref cursor);
        if (customCount is < 0 or > 1024) throw new InvalidDataException("Probe custom-version bound.");
        cursor = checked(cursor + 20 * customCount);
        SkipString(file, ref cursor);
        var length = Int32(file, ref cursor);
        if (length <= 0 || length > file.Length - cursor) throw new InvalidDataException("Probe payload bound.");
        var ciphertext = file.AsMemory(cursor, length);
        var prefix = Convert.FromHexString(profile ? ProfilePrefix : CharacterPrefix);
        var xor = Convert.FromHexString(profile ? ProfileXor : CharacterXor);
        var plain = new byte[length];
        var rebuilt = new byte[length];
        for (var i = 0; i < length; i++)
        {
            plain[i] = (byte)(ciphertext.Span[i] ^ xor[i % 32] ^ (i < 32 ? prefix[i] : ciphertext.Span[i - 32]));
            rebuilt[i] = (byte)(plain[i] ^ xor[i % 32] ^ (i < 32 ? prefix[i] : rebuilt[i - 32]));
        }
        if (!rebuilt.AsSpan().SequenceEqual(ciphertext.Span)) throw new InvalidDataException("Probe feedback inverse failed.");
        return plain;
    }

    /// <summary>Only callers' selected gameplay messages escape this probe; never dump whole payloads.</summary>
    internal static IReadOnlyList<WireRecord> SelectMessage(byte[] file, bool profile, params int[] tags)
    {
        ReadOnlyMemory<byte> selected = Decode(file, profile);
        foreach (var tag in tags)
        {
            var matches = Records(selected).Where(r => r.Tag == tag).ToArray();
            if (matches.Length == 0) return [];
            if (matches.Length != 1) throw new InvalidDataException("Probe singular-message ambiguity.");
            selected = matches[0].Data;
        }
        return Records(selected).ToArray();
    }

    public static SaveEvidence Read(byte[] file, bool wonderlands, bool profile)
    {
        var plain = Decode(file, profile);
        var serials = new List<byte[]>();
        ulong experience = 0, money = 0;
        foreach (var record in Records(plain))
        {
            if (!profile && record.Tag == 7) experience = Scalar(record.Data);
            if (!profile && record.Tag == 9)
            {
                var fields = Records(record.Data).ToArray();
                const uint moneyCategory = 618814354;
                if (Scalar(fields.Single(f => f.Tag == 1).Data) == moneyCategory)
                    money = fields.Where(f => f.Tag == 2).Select(f => Scalar(f.Data)).LastOrDefault();
            }
            if (!profile && record.Tag == 10 || profile && wonderlands && record.Tag == 212)
                serials.AddRange(Records(record.Data).Where(r => r.Tag == 1).Select(r => r.Data.ToArray()));
            else if (profile && (record.Tag == 138 || !wonderlands && record.Tag == 137)) serials.Add(record.Data.ToArray());
        }
        return new(experience, money, serials);
    }

    private static void SkipString(byte[] bytes, ref int cursor)
    {
        var length = Int32(bytes, ref cursor);
        if (length is < -16384 or > 16384) throw new InvalidDataException("Probe string bound.");
        cursor = checked(cursor + (length < 0 ? -2 * length : length));
        if (cursor > bytes.Length) throw new InvalidDataException("Probe string truncated.");
    }

    private static int Int32(byte[] bytes, ref int cursor)
    {
        if (cursor < 0 || cursor > bytes.Length - 4) throw new InvalidDataException("Probe header truncated.");
        var value = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(cursor, 4));
        cursor += 4;
        return value;
    }

    internal static IEnumerable<WireRecord> Records(ReadOnlyMemory<byte> memory)
    {
        var cursor = 0;
        var count = 0;
        while (cursor < memory.Length)
        {
            if (++count > MaximumRecords) throw new InvalidDataException("Probe record bound.");
            var tag = Varint(memory.Span, ref cursor);
            if (tag >> 3 is 0 or > 536870911) throw new InvalidDataException("Probe invalid tag.");
            var start = cursor;
            switch (tag & 7)
            {
                case 0: _ = Varint(memory.Span, ref cursor); break;
                case 1: cursor = checked(cursor + 8); break;
                case 2:
                    var length = Varint(memory.Span, ref cursor);
                    if (length > MaximumBytes) throw new InvalidDataException("Probe field bound.");
                    start = cursor;
                    cursor = checked(cursor + (int)length);
                    break;
                case 5: cursor = checked(cursor + 4); break;
                default: throw new InvalidDataException("Probe unsupported wire type.");
            }
            if (cursor > memory.Length) throw new InvalidDataException("Probe field truncated.");
            yield return new((int)(tag >> 3), memory.Slice(start, cursor - start));
        }
    }

    internal static ulong Scalar(ReadOnlyMemory<byte> bytes) { var position = 0; return Varint(bytes.Span, ref position); }
    internal static IEnumerable<ulong> Scalars(ReadOnlyMemory<byte> bytes)
    {
        var position = 0;
        while (position < bytes.Length) yield return Varint(bytes.Span, ref position);
    }
    private static ulong Varint(ReadOnlySpan<byte> bytes, ref int cursor)
    {
        ulong value = 0;
        for (var shift = 0; shift < 70; shift += 7)
        {
            if (cursor >= bytes.Length) throw new InvalidDataException("Probe varint truncated.");
            var next = bytes[cursor++];
            if (shift == 63 && next > 1) throw new InvalidDataException("Probe varint overflow.");
            value |= (ulong)(next & 127) << shift;
            if (next < 128) return value;
        }
        throw new InvalidDataException("Probe varint bound.");
    }
}

internal sealed record WireRecord(int Tag, ReadOnlyMemory<byte> Data);
internal sealed record SaveEvidence(ulong Experience, ulong Money, IReadOnlyList<byte[]> Serials);
