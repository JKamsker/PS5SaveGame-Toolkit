using System.Globalization;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing;

internal static class EquipmentViews
{
    public const string Source = "https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/vm/equipment.rs";
    public static string Provenance => $"Inferred relationships: ClayAmore / ER-Save-Editor, MIT, {Source}, retrieved 2026-10-28. "
        + "One PS5 snapshot; agreement does not prove complete equip dependencies, unlocked slots or legal loadouts.";

    public static ViewNode Build(byte[] bytes, SemanticCharacter character, ReferenceBrowser references) =>
        new TableNode([new("slot", "Candidate slot"), new("item", "Held identity"), new("handle", "Handle"),
            new("position", "Saved position / held record"), new("assembly", "Assembly ID"), new("equipped", "Equipped ID"),
            new("status", "Relationship check"), new("source", "Evidence / attribution")],
            EquipmentReader.Read(bytes, character).Select(link => Row(character.Slot, link, references)).ToArray())
        { Title = "Existing equipment relationships (read-only)", Description = Provenance };

    private static TableRow Row(int slot, EquipmentLink link, ReferenceBrowser references)
    {
        var record = link.Entry is { } entry ? references.Find(entry.RawId) : null;
        return new TableRow($"slots/{slot}/equipment-links/{link.Key}", new Dictionary<string, ViewNode>
        {
            ["slot"] = new TextNode(link.Label),
            ["item"] = new TextNode(record?.Name ?? link.Entry?.RawId ?? "Unresolved"),
            ["handle"] = Hex(link.Handle),
            ["position"] = new TextNode($"{link.Position.ToString(CultureInfo.InvariantCulture)}; held record {link.Entry?.Record.ToString(CultureInfo.InvariantCulture) ?? "—"}"),
            ["assembly"] = link.AssemblyId is { } assembly ? Hex(assembly) : new TextNode("Not present"),
            ["equipped"] = Hex(link.EquippedId),
            ["status"] = new TextNode(link.Status),
            ["source"] = new SectionNode("Relationship evidence", [new TextNode(Provenance),
                new TextNode(record is null ? "No compatible name record; raw identity retained." : ReferenceBrowser.Details(record))]),
        });
    }

    private static TextNode Hex(uint value) => new($"0x{value:X8}") { Monospace = true };
}
