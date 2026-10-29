using System.Globalization;
using SaveToolkit.Sdk.Diff;

namespace SaveToolkit.Plugins.EldenRing;

// Only approved numeric character/equipment fields; never the neighboring account/password buffers.
internal static class CharacterEvidence
{
    private static readonly string[] AttributeNames =
        ["vigor", "mind", "endurance", "strength", "dexterity", "intelligence", "faith", "arcane"];

    public static bool Add(byte[] bytes, CharacterSlot slot, SemanticCharacter character, ISaveDecodeSink sink)
    {
        var prefix = $"slots/{slot.Index}";
        bool Integer(string path, uint value) => sink.Add(prefix + path, "integer", value.ToString(CultureInfo.InvariantCulture));
        bool Hex(string path, uint value) => sink.Add(prefix + path, "text", $"0x{value:X8}");
        var player = slot.PlayerOffset!.Value;
        for (var index = 0; index < AttributeNames.Length; index++)
            if (!Integer("/attributes/" + AttributeNames[index],
                EldenRingParser.ReadUInt32(bytes, player + EldenRingLayout.AttributesOffset + index * sizeof(uint)))) return false;
        if (!Integer("/level", EldenRingParser.ReadUInt32(bytes, player + EldenRingLayout.LevelOffset))
            || !Integer("/profile-level", EldenRingParser.ReadUInt32(bytes, slot.ProfileOffset + EldenRingLayout.ProfileLevelOffset))) return false;
        foreach (var link in EquipmentReader.Read(bytes, character))
        {
            var path = "/equipment-links/" + link.Key;
            if (!Hex(path + "/handle", link.Handle) || !Integer(path + "/position", link.Position)
                || !Hex(path + "/equipped-id", link.EquippedId)) return false;
            if (link.AssemblyId is { } assembly && !Hex(path + "/assembly-id", assembly)) return false;
            if (link.Entry is { } entry && !sink.Add(prefix + path + "/identity", "text", entry.RawId)) return false;
            if (ProfileEquipmentReader.Read(bytes, slot, link) is { } profile
                && (!Hex(path + "/profile/encoded-id", profile.EncodedId) || !Hex(path + "/profile/item-id", profile.ItemId))) return false;
        }
        return true;
    }
}
