namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Current first-RESD arrays shared by views and immutable edit policies.</summary>
internal sealed record GameplayLayout(int InventoryOffset, int InventoryStride, uint GilType,
    int MateriaIdOffset, int MateriaLevelOffset, int PlayTimeOffset)
{
    public const int MateriaStride = 32;
    public const int QuantityDelta = 4;
    public const int MateriaApDelta = 4;
    public int MateriaApOffset => MateriaIdOffset + MateriaApDelta;

    private static readonly GameplayLayout Remake = new(0x34DD8, 24, 20, 0x1844, 0x1840, 0x2B0);
    private static readonly GameplayLayout Rebirth = new(0x4FA70, 32, 1, 0xBFA6C, 0xBFA78, 0x3C30);

    public static GameplayLayout For(Ff7Schema schema) => schema.IsRemake ? Remake : Rebirth;
}
