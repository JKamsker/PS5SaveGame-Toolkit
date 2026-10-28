using System.Buffers.Binary;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class InventoryStateTests
{
    [Theory]
    [InlineData("configs\\equipment\\wpn_burstpistol_item.config", "b33fb3994f5e7ec3")]
    [InlineData("configs/equipment/wpn_mrfunguy_item.config", "8d632a5622511f00")]
    [InlineData("CONFIGS//EQUIPMENT/WPN_WARMONGER_ITEM.CONFIG", "b29993da27515a36")]
    [InlineData("configs/equipment/wpn_warprifle_item.config", "87ab40964da66dda")]
    public void AssetPathVectorsMatchPs5InventoryConfigTokens(string path, string expected) =>
        Assert.Equal(expected, RiftAssetId.Hash(path));

    [Fact]
    public void UpgradeSlotsUseMeasuredBigEndianWidthsWithoutAssumingPurchasedNodes()
    {
        var bytes = Weapon();
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(44), 0x0102030405060708);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(52), 0x1234);
        bytes[84] = 3; bytes[116] = 7; bytes[118] = 1;
        var state = Assert.IsType<WeaponUpgrades>(InventoryState.Weapon(bytes));
        Assert.Equal(0x0102030405060708UL, state.Mods);
        Assert.Equal(16, state.Grid.Count);
        Assert.Equal(0x1234, state.Grid[0]);
        Assert.Equal(32, state.Counts.Count);
        Assert.Equal(3, state.Counts[0]);
        Assert.Equal(7, state.SelectedCell);
        Assert.True(state.Omega);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(117)]
    [InlineData(118)]
    public void UnprovenUpgradeLayoutsStayOpaque(int offset)
    {
        var bytes = Weapon();
        bytes[offset] = 2;
        Assert.Null(InventoryState.Weapon(bytes));
        Assert.Null(InventoryState.Weapon(bytes.AsSpan(0, 118)));
    }

    [Fact]
    public void VanityTailRequiresItsNestedMasksAndKeepsUnknownColorCodes()
    {
        var bytes = new byte[460];
        bytes[0] = 0xFF; bytes[1] = 0x1F; bytes[24] = 1; bytes[431] = 1; bytes[440] = 3; bytes[458] = 1;
        for (var i = 0; i < 4; i++) { bytes[29 + i * 21] = 7; bytes[311 + i * 30] = 15; bytes[316 + i * 30] = 7; }
        for (var i = 0; i < 6; i++) bytes[113 + i * 33] = 15;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(453), 0x01020304);
        bytes[457] = 9; bytes[459] = 255;
        var state = Assert.IsType<VanityState>(InventoryState.Vanity(bytes));
        Assert.Equal(0x01020304u, state.ExpirationTime);
        Assert.Equal(9, state.BeenPreviewed);
        Assert.Equal(255, state.Color);
        bytes[316] = 0;
        Assert.Null(InventoryState.Vanity(bytes));
    }

    private static byte[] Weapon()
    {
        var bytes = new byte[119];
        bytes[0] = 0xFF; bytes[1] = 0x3F;
        return bytes;
    }
}
