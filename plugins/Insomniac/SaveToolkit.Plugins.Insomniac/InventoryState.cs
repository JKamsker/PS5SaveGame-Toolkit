using System.Buffers.Binary;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed record WeaponUpgrades(ulong Mods, IReadOnlyList<ushort> Grid,
    IReadOnlyList<byte> Counts, byte SelectedCell, bool Omega);

internal sealed record VanityState(uint ExpirationTime, byte BeenPreviewed, byte Color);

/// <summary>Supported full presence masks only. Unfamiliar layouts stay opaque.
/// Schema field order is CC0; wire offsets/byte order are independently measured.</summary>
internal static class InventoryState
{
    private const int WeaponSize = 119;
    private const int GridOffset = 52;
    private const int GridRows = 16;
    private const int CountsOffset = 84;
    private const int CountSlots = 32;
    private const int SelectedOffset = 116;
    private const int TestCountOffset = 117;
    private const int OmegaOffset = 118;
    private const int VanitySize = 460;
    private const int VanityExpirationOffset = 453;
    private const int VanityPreviewOffset = 457;
    private const int VanityColorMaskOffset = 458;
    private const int LocatorOffset = 24;
    private const int TextureOffset = 29;
    private const int TextureSize = 21;
    private const int MaterialOffset = 113;
    private const int MaterialSize = 33;
    private const int ColorOverrideOffset = 311;
    private const int ColorOverrideSize = 30;
    private const int ColorVectorOffset = 316;
    private const int ConduitOffset = 431;
    private const int ModelVariantOffset = 440;
    private const int ModsOffset = 44;
    private const byte LocatorMask = 1;
    private const byte TextureMask = 7;
    private const byte MaterialMask = 15;
    private const byte ColorOverrideMask = 15;
    private const byte ColorVectorMask = 7;
    private const byte ConduitMask = 1;
    private const byte ModelVariantMask = 3;
    private const int ColorOverrideCount = 4;
    private const int MaterialOverrideCount = 6;

    internal static WeaponUpgrades? Weapon(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != WeaponSize || payload[0] != 0xFF || payload[1] != 0x3F ||
            payload[TestCountOffset] != 0 || payload[OmegaOffset] > 1) return null;
        var grid = new ushort[GridRows];
        for (var row = 0; row < grid.Length; row++)
            grid[row] = BinaryPrimitives.ReadUInt16BigEndian(payload[(GridOffset + row * sizeof(ushort))..]);
        return new(BinaryPrimitives.ReadUInt64BigEndian(payload[ModsOffset..]), grid,
            payload.Slice(CountsOffset, CountSlots).ToArray(), payload[SelectedOffset], payload[OmegaOffset] != 0);
    }

    internal static VanityState? Vanity(ReadOnlySpan<byte> payload)
    {
        // The nested Color object uses a presence byte followed by ColorId.
        // Require its measured framing rather than interpreting arbitrary tails.
        if (payload.Length != VanitySize || payload[0] != 0xFF || payload[1] != 0x1F ||
            payload[VanityColorMaskOffset] != 1) return null;
        if (payload[LocatorOffset] != LocatorMask || payload[ConduitOffset] != ConduitMask ||
            payload[ModelVariantOffset] != ModelVariantMask) return null;
        for (var index = 0; index < ColorOverrideCount; index++)
            if (payload[TextureOffset + index * TextureSize] != TextureMask ||
                payload[ColorOverrideOffset + index * ColorOverrideSize] != ColorOverrideMask ||
                payload[ColorVectorOffset + index * ColorOverrideSize] != ColorVectorMask) return null;
        for (var index = 0; index < MaterialOverrideCount; index++)
            if (payload[MaterialOffset + index * MaterialSize] != MaterialMask) return null;
        return new(BinaryPrimitives.ReadUInt32BigEndian(payload[VanityExpirationOffset..]),
            payload[VanityPreviewOffset], payload[VanityColorMaskOffset + 1]);
    }
}
