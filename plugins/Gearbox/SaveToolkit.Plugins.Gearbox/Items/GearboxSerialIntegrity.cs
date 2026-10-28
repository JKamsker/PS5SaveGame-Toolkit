using System.Security.Cryptography;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Unchanged opaque/corrupt originals remain lossless; every new serial must validate.</summary>
internal sealed class GearboxSerialIntegrity
{
    private readonly HashSet<string> _original;
    public GearboxSerialIntegrity(ProtoMessage root, bool profile)
    {
        var fields = profile ? root.All(GearboxInventory.Bank).Concat(root.All(GearboxInventory.LostLoot)) : root.All(GearboxInventory.Backpack);
        _original = fields.Select(f => Digest(InventoryEntry.SerialOf(f))).ToHashSet(StringComparer.Ordinal);
    }

    public void Validate(GearboxInventory inventory, bool wonderlands)
    {
        foreach (var entry in inventory.Entries)
            if (!_original.Contains(Digest(entry.Serial))) _ = SerialEnvelope.Decode(entry.Serial, wonderlands);
    }

    private static string Digest(byte[] serial) => Convert.ToHexString(SHA256.HashData(serial));
}
