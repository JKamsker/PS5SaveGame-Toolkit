namespace SaveToolkit.Plugins.EldenRing;

// ClayAmore 014107f0, save/common/save_slot.rs. Deeper reads are gated to sampled v150.
internal static class SemanticLayout
{
    public const int PlayerSize = 0x1B0;
    public const int PlayerPadding = 0xD0;
    public const int EquipmentSize = 0x58;
    public const int AssemblySize = 0x74;
    public const int HeldCommonCapacity = 0xA80;
    public const int HeldKeyCapacity = 0x180;
    public const int StorageCommonCapacity = 0x780;
    public const int StorageKeyCapacity = 0x80;
    public const int InventoryRowSize = 12;
    public const int InventoryOverhead = 16;
    public const int MagicSize = 0x74;
    public const int QuickItemsSize = 0x8C;
    public const int EquippedGesturesSize = 0x18;
    public const int ProjectileSize = 8;
    public const int EquippedItemsSize = 0x9C;
    public const int PhysicsSize = 8;
    public const int FaceSize = 0x12F;
    public const int GesturesSize = 0x100;
    public const int RideSize = 0x28;
    public const int RegionTailSize = 1 + 0x40 + 12 + 0x1008 + 0x34;
    public const int AcquisitionCapacity = 0x1B58;
    public const int AcquisitionRecordSize = 4 * sizeof(uint);
    public const int AcquisitionSize = 2 * sizeof(uint) + AcquisitionCapacity * AcquisitionRecordSize;
    public const string AcquisitionRangeName = "Acquisition candidate";
    public const int TutorialTailSize = 0x408 + 0x1D;
    public const int EventSize = 0x1BF99F;
    public const string EventRangeName = "Event flags candidate (no status mapping)";
    public const int ClassOffset = 0xB7;
    public const uint HandleMask = 0xF0000000;
    public const uint IdentityMask = 0x0FFFFFFF;
    public const uint WeaponHandle = 0x80000000;
    public const uint ArmorHandle = 0x90000000;
    public const uint TalismanHandle = 0xA0000000;
    public const uint GoodsHandle = 0xB0000000;
    public const uint AshHandle = 0xC0000000;
    public const uint TalismanId = 0x20000000;
    public const uint GoodsId = 0x40000000;
    public const uint AshId = 0x80000000;

    public static int InventorySize(int common, int key) => InventoryOverhead + (common + key) * InventoryRowSize;
}
