namespace SaveToolkit.Plugins.FinalFantasy7;

internal static class CharacterLayout
{
    public const int BaseOffset = 0x30;
    public const int RemakeStride = 0x40;
    public const int RebirthStride = 0x100;
    public const int HpDelta = 0x10;
    public const int MpDelta = 0x18;
    public const int ExperienceDelta = 0x20;
    public const int MaximumVitalDelta = 4;
    private const int RebirthWeaponDelta = 0x9C;

    public static int Offset(Ff7Schema schema, int character) =>
        BaseOffset + character * (schema.IsRemake ? RemakeStride : RebirthStride);

    public static int WeaponOffset(int character) => BaseOffset + character * RebirthStride + RebirthWeaponDelta;
}
