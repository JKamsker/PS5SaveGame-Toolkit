// Adapted from apocalyptech BL3/TTWL datalib.py (zlib). This is an altered C# implementation.
// Copyright (c) 2020-2022 CJ Kucera, Abram Hindle. See Notices/*-COPYING.txt and README attribution.
using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Serial envelope only: bounded de-obfuscation, folded CRC, original-seed inverse.</summary>
internal sealed class SerialEnvelope
{
    public const int MaximumLength = 4096;
    private const ulong Multiplier = 0x10A860C1;
    private const ulong Modulus = 0xFFFFFFFB;
    private const uint CrcPolynomial = 0xEDB88320;
    private static readonly uint[] CrcTable = BuildCrcTable();
    private readonly byte[] _header;
    private readonly byte[] _plain;
    public int Version => _header[0];
    public int Seed => BinaryPrimitives.ReadInt32BigEndian(_header.AsSpan(1));
    public ReadOnlyMemory<byte> Payload => _plain.AsMemory(2);

    private SerialEnvelope(byte[] header, byte[] plain) { _header = header; _plain = plain; }

    public static SerialEnvelope Decode(ReadOnlySpan<byte> serial, bool wonderlands)
    {
        if (serial.Length is < 8 or > MaximumLength)
            throw new SaveFormatException("Item serial length is outside the supported bounds.");
        if (wonderlands ? serial[0] != 5 : serial[0] is not (3 or 4))
            throw new SaveFormatException("Unsupported serial version for this game.");
        var seed = BinaryPrimitives.ReadInt32BigEndian(serial[1..5]);
        var plain = new byte[serial.Length - 5];
        var steps = (seed & 31) % plain.Length;
        ulong state = unchecked((uint)(seed >> 5));
        for (var i = 0; i < plain.Length; i++)
        {
            var value = serial[5 + i];
            if (seed != 0) { state = state * Multiplier % Modulus; value ^= (byte)state; }
            plain[(i + steps) % plain.Length] = value;
        }
        var result = new SerialEnvelope(serial[..5].ToArray(), plain);
        if (BinaryPrimitives.ReadUInt16BigEndian(plain) != result.Checksum())
            throw new SaveFormatException("Item serial checksum mismatch; bytes preserved read-only.");
        return result;
    }

    private ushort Checksum()
    {
        var crc = uint.MaxValue;
        foreach (var b in _header) crc = Update(crc, b);
        crc = Update(Update(crc, byte.MaxValue), byte.MaxValue);
        foreach (var b in _plain.AsSpan(2)) crc = Update(crc, b);
        crc = ~crc;
        return (ushort)((crc >> 16) ^ crc);
    }

    private static uint Update(uint crc, byte value) => (crc >> 8) ^ CrcTable[(crc ^ value) & byte.MaxValue];

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var i = 0; i < table.Length; i++)
        {
            var crc = (uint)i;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? CrcPolynomial : 0);
            table[i] = crc;
        }
        return table;
    }

    public byte[] EncodeOriginalSeed()
    {
        var result = new byte[_header.Length + _plain.Length];
        _header.CopyTo(result, 0);
        var steps = (Seed & 31) % _plain.Length;
        ulong state = unchecked((uint)(Seed >> 5));
        for (var i = 0; i < _plain.Length; i++)
        {
            var value = _plain[(i + steps) % _plain.Length];
            if (Seed != 0) { state = state * Multiplier % Modulus; value ^= (byte)state; }
            result[_header.Length + i] = value;
        }
        return result;
    }

    public byte[] Patch(int offset, int width, int value)
    {
        if (offset < 0 || width is < 1 or > 16 || offset > Payload.Length * 8 - width || value < 0 || value >= 1 << width)
            throw new ArgumentOutOfRangeException(nameof(value));
        var changed = new SerialEnvelope(_header.ToArray(), _plain.ToArray());
        for (var bit = 0; bit < width; bit++)
        {
            var position = offset + bit;
            var mask = 1 << (position % 8);
            changed._plain[2 + position / 8] = (byte)((changed._plain[2 + position / 8] & ~mask) | (((value >> bit) & 1) * mask));
        }
        BinaryPrimitives.WriteUInt16BigEndian(changed._plain, changed.Checksum());
        return changed.EncodeOriginalSeed();
    }
}
