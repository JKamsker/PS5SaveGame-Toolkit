namespace SaveToolkit.Plugins.EldenRing;

// Format facts from ClayAmore's PlayStation reader, checked against PPSA03169.
internal static class EldenRingLayout
{
    public const uint Magic = 0x2C9C01CB;
    public const uint Sentinel = 0x7F7F7F7F;
    public const int SentinelOffset = 8;
    public const int HeaderSize = 0x70;
    public const int SlotCount = 10;
    public const int SlotSize = 0x280000;
    public const int UserDataOffset = HeaderSize + SlotCount * SlotSize;
    public const int UserDataSize = 0x60000;
    public const int RegulationOffset = UserDataOffset + UserDataSize;
    public const int RegulationSize = 0x240010;
    public const int MinimumFileSize = RegulationOffset + RegulationSize;

    public const int SlotPlayTimeOffset = 8;
    public const int ItemMapOffset = 0x20;
    public const int ItemCount = 0x1400;
    public const int LegacyItemCount = ItemCount - 2;
    public const uint LastLegacyVersion = 81;
    public const int ItemHeaderSize = 8;
    public const int WeaponExtraSize = 13;
    public const int ArmorExtraSize = 8;
    public const uint ItemTypeMask = 0xF0000000;
    public const uint ArmorType = 0x10000000;

    public const int AttributesOffset = 0x34;
    public const int AttributeCount = 8;
    public const int LevelOffset = 0x60;
    public const int RunesOffset = 0x64;
    public const int RuneMemoryOffset = 0x68;
    public const int NameOffset = 0x94;
    public const int NameCharacters = 16;
    public const int NameBytes = (NameCharacters + 1) * sizeof(ushort);
    public const uint MaximumAttribute = 99;
    public const uint LevelAdjustment = 79;
    public const uint MaximumLevel = MaximumAttribute * AttributeCount - LevelAdjustment;
    public const uint MaximumRunes = 999_999_999;
    public const ulong MillisecondsPerSecond = 1000;

    public const int MenuOffset = UserDataOffset + 0x14C;
    public const int MenuHeaderSize = 8;
    public const int ProfileSize = 0x24C;
    public const int ProfileLevelOffset = 0x22;
    public const int ProfilePlayTimeOffset = 0x26;
    public const int ProfileRuneMemoryOffset = 0x2A;
    public const uint WritableSlotVersion = 150;
    public const uint WritableProfileVersion = 90;
}
