using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal sealed record FaithBlock(int Index, int Offset, uint Type, int DeclaredSize, ulong Version)
{
    public const int HeaderSize = 16;
    public const int SerializedSizeBias = 8;
    public const int SizeOffset = sizeof(uint);
    public const int VersionOffset = 2 * sizeof(uint);
    public int PayloadOffset => Offset + HeaderSize;
    public int PayloadLength => DeclaredSize - HeaderSize - SerializedSizeBias;
    public string Name => Type switch
    {
        1 => "PlayerData", 2 => "PartyData", 3 => "QuestData", 4 => "BattleDirectorData",
        5 => "SoundConfigData", 6 => "ItemData", 7 => "UserData", 10 => "CameraConfigData",
        14 => "HudConfigData", 16 => "WorldMapConfigData", 17 => "PadConfigData",
        25 => "SystemConfigData", 27 => "SkillData", 28 => "SystemUserData",
        45 => "VideoConfigData", 70 => "KeybindConfigData", _ => $"Type {Type} (unmapped)",
    };
}

internal sealed record FaithContainer(ulong Version, IReadOnlyList<FaithBlock> Blocks, int TrailerOffset)
{
    public const int HeaderSize = 16;
    public const int CrcOffset = sizeof(uint);
    public const int VersionOffset = 2 * sizeof(uint);
    public const int MaximumFileSize = 64 * 1024 * 1024;
    public const ulong GameVersion = 0x785930BD;
    public const ulong SystemVersion = 0x0C044C6D;
    private const int MaximumBlocks = 4096;
    private const int MinimumBlockSize = FaithBlock.HeaderSize + FaithBlock.SerializedSizeBias;
    private const int BlockAlignment = 8;

    public bool IsSystem => Version == SystemVersion;

    public static FaithContainer Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize + sizeof(uint) || bytes.Length > MaximumFileSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes) != HeaderSize)
        {
            throw new SaveFormatException("Invalid Faith header size or truncated save.");
        }

        var version = BinaryPrimitives.ReadUInt64LittleEndian(bytes[VersionOffset..]);
        if (version is not (GameVersion or SystemVersion))
        {
            throw new SaveFormatException("Unsupported Faith container version (expected plaintext PS5 data).");
        }

        var expected = BinaryPrimitives.ReadUInt32LittleEndian(bytes[CrcOffset..]);
        if (expected != FaithCrc32.Compute(bytes[HeaderSize..]))
        {
            throw new SaveFormatException("Faith CRC32 mismatch: the save is damaged or still wrapped/encrypted.");
        }

        var blocks = new List<FaithBlock>();
        var position = HeaderSize;
        while (position <= bytes.Length - sizeof(uint))
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(bytes[position..]);
            if (type == 0)
            {
                if (blocks.Count == 0)
                {
                    throw new SaveFormatException("Faith save contains no data blocks.");
                }

                // The terminator and any unrecognised trailing bytes are preserved verbatim.
                return new FaithContainer(version, blocks, position);
            }

            if (blocks.Count >= MaximumBlocks || bytes.Length - position < FaithBlock.HeaderSize)
            {
                throw new SaveFormatException($"Truncated or excessive Faith block headers at 0x{position:X}.");
            }

            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(position + FaithBlock.SizeOffset)..]);
            if (size < MinimumBlockSize || size % BlockAlignment != 0 ||
                size - FaithBlock.SerializedSizeBias > bytes.Length - position)
            {
                throw new SaveFormatException($"Invalid Faith block size at 0x{position:X}.");
            }

            blocks.Add(new FaithBlock(blocks.Count, position, type, (int)size,
                BinaryPrimitives.ReadUInt64LittleEndian(bytes[(position + FaithBlock.VersionOffset)..])));
            position += (int)size - FaithBlock.SerializedSizeBias;
        }

        throw new SaveFormatException("Faith block terminator is missing.");
    }
}

internal static class FaithCrc32
{
    private const uint ReflectedPolynomial = 0xEDB88320;
    private const uint InitialValue = uint.MaxValue;
    private const int BitsPerByte = 8;

    // CRC-32/ISO-HDLC, including the final complement; no shared mutable lookup table.
    public static uint Compute(ReadOnlySpan<byte> bytes)
    {
        var crc = InitialValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < BitsPerByte; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : ReflectedPolynomial);
            }
        }

        return ~crc;
    }
}
