using System.Security.Cryptography;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Unchanged opaque/corrupt originals remain lossless; every new serial must validate.</summary>
internal sealed class GearboxSerialIntegrity
{
    private readonly HashSet<string> _original;
    public GearboxSerialIntegrity(ProtoMessage root, bool profile)
    {
        _original = Serials(root, profile).Select(Digest).ToHashSet(StringComparer.Ordinal);
    }

    public void Validate(ProtoMessage root, bool profile)
    {
        // Validation needs bytes, not session identities. Keep inventory indexing lazy on F7 open.
        foreach (var serial in Serials(root, profile))
            if (!_original.Contains(Digest(serial))) _ = SerialEnvelope.Decode(serial, root.Schema.Wonderlands);
    }

    private static IEnumerable<byte[]> Serials(ProtoMessage root, bool profile) =>
        (profile ? root.All(GearboxInventory.Bank).Concat(root.All(GearboxInventory.LostLoot)) : root.All(GearboxInventory.Backpack))
        .Select(InventoryEntry.SerialOf);

    private static string Digest(byte[] serial) => Convert.ToHexString(SHA256.HashData(serial));
}
