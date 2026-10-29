namespace SaveToolkit.Plugins.EldenRing;

internal sealed record ProfileEquipment(uint EncodedId, uint ItemId, int EncodedOffset, int ItemOffset, string Status);

// v150/profile-90 PS5 observations, not a port of the upstream profile's differing gap words.
// Encoded IDs are not instance handles: both sampled Scimitars share the same menu value.
internal static class ProfileEquipmentReader
{
    public const string Source = "https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/common/user_data_10.rs";
    public const string Provenance = "Inferred PS5 v150/profile-90 menu equipment candidates; independently checked on one snapshot. "
        + "Source: " + Source + " (MIT; accessed 2026-10-29). Source gap words differ from this sample. "
        + "Encoding agreement does not establish a safe equipment writer.";
    private const int EncodedGearOffset = 0x17E;
    private const int ItemGearOffset = 0x1D6;
    private const uint EncodedMarker = 0x80000000;
    private const uint WeaponIdentityMask = 0x007FFFFF;

    public static ProfileEquipment? Read(byte[] bytes, CharacterSlot slot, EquipmentLink link)
    {
        const string gearPrefix = "gear/";
        if (slot.Version != EldenRingLayout.WritableSlotVersion
            || EldenRingParser.ReadUInt32(bytes, EldenRingLayout.UserDataOffset) != EldenRingLayout.WritableProfileVersion
            || !link.Key.StartsWith(gearPrefix, StringComparison.Ordinal)) return null;
        var word = int.Parse(link.Key.AsSpan(gearPrefix.Length), System.Globalization.CultureInfo.InvariantCulture);
        var encodedOffset = slot.ProfileOffset + EncodedGearOffset + word * sizeof(uint);
        var itemOffset = slot.ProfileOffset + ItemGearOffset + word * sizeof(uint);
        var encoded = EldenRingParser.ReadUInt32(bytes, encodedOffset);
        var item = EldenRingParser.ReadUInt32(bytes, itemOffset);
        var status = "Unresolved live identity; empty/unarmed profile rules unverified";
        if (link.Entry?.ItemId is { } id)
        {
            var expectedEncoded = EncodedMarker | (word < EquipmentSlots.WeaponSlotCount ? id & WeaponIdentityMask : id);
            status = encoded == expectedEncoded && item == (id & SemanticLayout.IdentityMask)
                ? "Observed profile encodings agree; writes disabled"
                : "Profile representation disagreement; preserved without repair";
        }
        return new(encoded, item, encodedOffset, itemOffset, status);
    }
}
