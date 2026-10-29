using System.Globalization;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.EldenRing;

internal static class EquipmentViews
{
    public const string Source = "https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/vm/equipment.rs";
    public static string Provenance => $"Inferred relationships: ClayAmore / ER-Save-Editor, MIT, {Source}, retrieved 2026-10-28. "
        + "One PS5 snapshot; agreement does not prove complete equip dependencies, unlocked slots or legal loadouts.";

    public static ViewNode Build(byte[] bytes, SemanticCharacter character, CharacterSlot slot, ReferenceBrowser references,
        Func<string, string, FieldSpec, Func<FieldValue>, FieldNode> bind) =>
        new TableNode([new("slot", "Candidate slot"), new("item", "Held identity"), new("handle", "Handle"),
            new("position", "Saved position / held record"), new("assembly", "Assembly ID"), new("equipped", "Equipped ID"),
            new("status", "Relationship check"), new("profile-encoded", "Menu encoded ID"), new("profile-item", "Menu item ID"),
            new("profile-status", "Menu relationship check"), new("source", "Evidence / attribution")],
            EquipmentReader.Read(bytes, character).Select(link => Row(slot, link, ProfileEquipmentReader.Read(bytes, slot, link), references, bind)).ToArray())
        { Title = "Existing equipment relationships (read-only)", Description = Provenance };

    private static TableRow Row(CharacterSlot slot, EquipmentLink link, ProfileEquipment? profile, ReferenceBrowser references,
        Func<string, string, FieldSpec, Func<FieldValue>, FieldNode> bind)
    {
        var record = link.Entry is { } entry ? references.Find(entry.RawId) : null;
        var prefix = $"slots/{slot.Index}/equipment-links/{link.Key}";
        ViewNode ProfileField(string key, string label, uint value) => bind(prefix + "/profile/" + key, label,
            IntegerSpec.UInt32 with { Format = IntegerFormat.Hex }, () => FieldValue.Of((long)value)) with
            { Confidence = Confidence.Inferred, Description = ProfileEquipmentReader.Provenance };
        return new TableRow(prefix, new Dictionary<string, ViewNode>
        {
            ["slot"] = new TextNode(link.Label),
            ["item"] = link.Entry is null ? new TextNode("Unresolved")
                : bind(prefix + "/item", record?.Name ?? link.Entry.RawId,
                    new ReferenceSpec(references.FieldCatalogId(link.Entry.RawId)), () => FieldValue.Of(link.Entry.RawId)) with
                {
                    Confidence = link.Entry.ItemId.HasValue ? Confidence.Inferred : Confidence.Unknown,
                    Description = Provenance + " " + (record is null ? "No compatible name record; raw identity retained."
                        : references.ItemDetails(link.Entry.RawId)),
                },
            ["handle"] = Hex(link.Handle),
            ["position"] = new TextNode($"{link.Position.ToString(CultureInfo.InvariantCulture)}; held record {link.Entry?.Record.ToString(CultureInfo.InvariantCulture) ?? "—"}"),
            ["assembly"] = link.AssemblyId is { } assembly ? Hex(assembly) : new TextNode("Not present"),
            ["equipped"] = Hex(link.EquippedId),
            ["status"] = new TextNode(link.Status),
            ["profile-encoded"] = profile is null ? new TextNode("Not modeled")
                : ProfileField("encoded-id", "Menu encoded ID", profile.EncodedId),
            ["profile-item"] = profile is null ? new TextNode("Not modeled")
                : ProfileField("item-id", "Menu item ID", profile.ItemId),
            ["profile-status"] = new TextNode(profile?.Status ?? "Quick/pouch or unsupported profile version"),
            ["source"] = new SectionNode("Relationship evidence", [new TextNode(Provenance),
                new TextNode(ProfileEquipmentReader.Provenance),
                new TextNode(record is null ? "No compatible name record; raw identity retained." : references.ItemDetails(record.Key))]),
        });
    }

    private static TextNode Hex(uint value) => new($"0x{value:X8}") { Monospace = true };
}
