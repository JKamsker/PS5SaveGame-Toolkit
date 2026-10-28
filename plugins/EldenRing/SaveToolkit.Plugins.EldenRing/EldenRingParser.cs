using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.EldenRing;

internal static class EldenRingParser
{
    private static readonly UnicodeEncoding StrictUnicode = new(false, false, true);

    public static bool HasHeader(ReadOnlySpan<byte> bytes) => bytes.Length >= EldenRingLayout.HeaderSize
        && ReadUInt32(bytes, 0) == EldenRingLayout.Magic
        && ReadUInt32(bytes, EldenRingLayout.SentinelOffset) == EldenRingLayout.Sentinel;

    public static ParsedSave Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < EldenRingLayout.MinimumFileSize || !HasHeader(bytes))
        {
            throw new SaveFormatException("Invalid or truncated Elden Ring PlayStation memory.dat allocation.");
        }

        var profileVersion = ReadUInt32(bytes, EldenRingLayout.UserDataOffset);
        if (profileVersion == 0)
        {
            throw new SaveFormatException("Missing Elden Ring user-data version.");
        }

        var menuLength = ReadUInt32(bytes, EldenRingLayout.MenuOffset + sizeof(uint));
        const int menuDataOffset = EldenRingLayout.MenuOffset + EldenRingLayout.MenuHeaderSize;
        const int maximumMenuLength = EldenRingLayout.RegulationOffset - menuDataOffset
            - EldenRingLayout.SlotCount * (EldenRingLayout.ProfileSize + 1);
        if (menuLength > maximumMenuLength)
        {
            throw new SaveFormatException("The menu-data length extends beyond the user-data block.");
        }

        var activeOffset = menuDataOffset + (int)menuLength;
        var profilesOffset = activeOffset + EldenRingLayout.SlotCount;
        var slots = new CharacterSlot[EldenRingLayout.SlotCount];
        for (var index = 0; index < slots.Length; index++)
        {
            var active = bytes[activeOffset + index];
            if (active > 1)
            {
                throw new SaveFormatException($"Slot {index}: invalid active-profile flag.");
            }

            var offset = EldenRingLayout.HeaderSize + index * EldenRingLayout.SlotSize;
            var version = ReadUInt32(bytes, offset);
            var profile = profilesOffset + index * EldenRingLayout.ProfileSize;
            int? player = null;
            if (active != 0)
            {
                if (version == 0)
                {
                    throw new SaveFormatException($"Slot {index}: active profile has an empty character block.");
                }

                var data = bytes.Slice(offset, EldenRingLayout.SlotSize);
                player = offset + FindPlayerData(data, version);
                ValidateCharacter(bytes, index, offset, player.Value, profile);
            }

            // Inactive slots may retain deleted-character bytes; never interpret or clear them.
            slots[index] = new CharacterSlot(index, offset, version, player, profile);
        }

        return new ParsedSave(profileVersion, slots);
    }

    public static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);

    public static ulong ReadUInt64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]);

    public static string ReadName(ReadOnlySpan<byte> bytes, int offset)
    {
        var name = bytes.Slice(offset, EldenRingLayout.NameBytes);
        for (var length = 0; length < name.Length; length += sizeof(ushort))
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(name[length..]) != 0)
            {
                continue;
            }

            try
            {
                return StrictUnicode.GetString(name[..length]);
            }
            catch (DecoderFallbackException exception)
            {
                throw new SaveFormatException("Character name contains invalid UTF-16.", exception);
            }
        }

        throw new SaveFormatException("Character name is missing its UTF-16 terminator.");
    }

    private static int FindPlayerData(ReadOnlySpan<byte> slot, uint version)
    {
        var itemCount = version <= EldenRingLayout.LastLegacyVersion
            ? EldenRingLayout.LegacyItemCount : EldenRingLayout.ItemCount;
        var cursor = EldenRingLayout.ItemMapOffset;
        for (var index = 0; index < itemCount; index++)
        {
            var itemId = ReadUInt32(slot, cursor + sizeof(uint));
            cursor += EldenRingLayout.ItemHeaderSize;
            if (itemId == 0)
            {
                continue;
            }

            cursor += (itemId & EldenRingLayout.ItemTypeMask) switch
            {
                0 => EldenRingLayout.WeaponExtraSize,
                EldenRingLayout.ArmorType => EldenRingLayout.ArmorExtraSize,
                _ => 0,
            };
        }

        // Even 5120 maximum-size entries fit comfortably in a 0x280000-byte slot.
        return cursor;
    }

    private static void ValidateCharacter(ReadOnlySpan<byte> bytes, int index, int slot, int player, int profile)
    {
        var name = ReadName(bytes, player + EldenRingLayout.NameOffset);
        var level = ReadUInt32(bytes, player + EldenRingLayout.LevelOffset);
        if (string.IsNullOrWhiteSpace(name) || name != ReadName(bytes, profile)
            || level is 0 or > EldenRingLayout.MaximumLevel
            || level != ReadUInt32(bytes, profile + EldenRingLayout.ProfileLevelOffset))
        {
            throw new SaveFormatException($"Slot {index}: character name/level disagrees with its profile, or the layout is unsupported.");
        }

        uint attributeSum = 0;
        for (var attribute = 0; attribute < EldenRingLayout.AttributeCount; attribute++)
        {
            var value = ReadUInt32(bytes, player + EldenRingLayout.AttributesOffset + attribute * sizeof(uint));
            if (value is 0 or > EldenRingLayout.MaximumAttribute)
            {
                throw new SaveFormatException($"Slot {index}: an attribute is outside the supported range 1–99.");
            }

            attributeSum += value;
        }

        if (attributeSum != level + EldenRingLayout.LevelAdjustment)
        {
            throw new SaveFormatException($"Slot {index}: level must equal the sum of the eight attributes minus 79.");
        }

        var played = ReadUInt64(bytes, slot + EldenRingLayout.SlotPlayTimeOffset);
        if (played / EldenRingLayout.MillisecondsPerSecond != ReadUInt32(bytes, profile + EldenRingLayout.ProfilePlayTimeOffset))
        {
            throw new SaveFormatException($"Slot {index}: character play time disagrees with its profile.");
        }

        var memory = ReadUInt32(bytes, player + EldenRingLayout.RuneMemoryOffset);
        var runes = ReadUInt32(bytes, player + EldenRingLayout.RunesOffset);
        if (memory != ReadUInt32(bytes, profile + EldenRingLayout.ProfileRuneMemoryOffset)
            || runes > memory || runes > EldenRingLayout.MaximumRunes)
        {
            throw new SaveFormatException($"Slot {index}: rune history is inconsistent or held runes exceed the supported limit.");
        }
    }
}
