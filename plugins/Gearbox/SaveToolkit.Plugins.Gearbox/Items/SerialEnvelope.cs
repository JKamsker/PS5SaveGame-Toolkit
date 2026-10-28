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
        var mixed = Xor(serial[5..], seed);
        var steps = (seed & 31) % mixed.Length;
        var plain = new byte[mixed.Length];
        for (var i = 0; i < mixed.Length; i++) plain[(i + steps) % plain.Length] = mixed[i];
        var result = new SerialEnvelope(serial[..5].ToArray(), plain);
        if (BinaryPrimitives.ReadUInt16BigEndian(plain) != result.Checksum())
            throw new SaveFormatException("Item serial checksum mismatch; bytes preserved read-only.");
        return result;
    }

    private static byte[] Xor(ReadOnlySpan<byte> input, int seed)
    {
        var output = input.ToArray();
        ulong state = unchecked((uint)(seed >> 5));
        if (seed == 0) return output;
        for (var i = 0; i < output.Length; i++)
        {
            state = state * Multiplier % Modulus;
            output[i] ^= (byte)state;
        }
        return output;
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

    private static uint Update(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? CrcPolynomial : 0);
        return crc;
    }

    public byte[] EncodeOriginalSeed()
    {
        var rotated = new byte[_plain.Length];
        var steps = (Seed & 31) % _plain.Length;
        for (var i = 0; i < rotated.Length; i++) rotated[i] = _plain[(i + steps) % _plain.Length];
        return [.. _header, .. Xor(rotated, Seed)];
    }
}
