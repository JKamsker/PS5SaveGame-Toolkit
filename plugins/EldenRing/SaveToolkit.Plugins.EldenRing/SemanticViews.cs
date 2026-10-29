using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.EldenRing;

internal sealed class SemanticViews(byte[] bytes, IReadOnlyList<SemanticCharacter> characters, ReferenceBrowser references,
    IReferenceCatalog? classes)
{
    internal const string FormatSource = "https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/common/save_slot.rs";
    internal static string Provenance => $"Inferred from ClayAmore/ER-Save-Editor, MIT, {FormatSource} (retrieved 2026-10-28). "
        + "Checked on one PS5 v150 snapshot; no controlled change or console validation.";

    public IReadOnlyList<ViewNode> Character(CharacterSlot slot, Func<string, string, FieldSpec, Func<FieldValue>, FieldNode> bind)
    {
        var character = characters.FirstOrDefault(character => character.Slot == slot.Index);
        if (character is null)
            return [new TextNode("Deeper semantic reading is restricted to sampled character format 150; raw inspection remains available.")];
        var player = slot.PlayerOffset!.Value;
        var classId = bytes[player + SemanticLayout.ClassOffset].ToString(System.Globalization.CultureInfo.InvariantCulture);
        var classReference = classes?.Find(classId);
        var fields = new List<ViewNode>();
        (string Key, string Label, int Offset)[] vitals = [("hp", "HP", 8), ("fp", "FP", 20), ("stamina", "Stamina", 36)];
        string[] parts = ["current", "effective-max", "base-max"];
        foreach (var (key, label, offset) in vitals)
        for (var part = 0; part < parts.Length; part++)
        {
            var position = player + offset + part * sizeof(uint);
            fields.Add(bind($"slots/{slot.Index}/vitals/{key}/{parts[part]}", $"{label} · {parts[part]}", IntegerSpec.UInt32,
                () => FieldValue.Of((long)EldenRingParser.ReadUInt32(bytes, position))) with
            { Confidence = Confidence.Inferred, Description = Provenance, Warning = "Effective and base maxima can differ. No recalculation or editing." });
        }
        return
        [
            new SectionNode("Vital candidates", fields) { Description = Provenance, Collapsed = true },
            bind($"slots/{slot.Index}/class-id", "Archetype byte (class candidate)", IntegerSpec.UInt8,
                () => FieldValue.Of((long)bytes[player + SemanticLayout.ClassOffset])) with
            { Confidence = Confidence.Inferred, Description = Provenance },
            bind($"slots/{slot.Index}/class-reference", "Class candidate", new ReferenceSpec(ProgressReferences.ClassCatalogId),
                () => FieldValue.Of(classId)) with
            {
                Confidence = Confidence.Inferred,
                Description = classReference is null ? "Unknown class name; exact raw archetype ID preserved. " + Provenance
                    : classReference.Name + ": starting values are source references, not validated respec minima. "
                        + string.Join(", ", classReference.Attributes.Where(pair => pair.Key is "level" or "vigor" or "mind" or "endurance"
                            or "strength" or "dexterity" or "intelligence" or "faith" or "arcane").Select(pair => pair.Key + "=" + pair.Value))
                        + ". " + ReferenceBrowser.Details(classReference),
            },
            EquipmentViews.Build(bytes, character, references, bind),
            new SectionNode("Equipment raw representations", Equipment(character, bind)) { Description = Provenance, Collapsed = true },
            new TableNode([new("name", "Range"), new("offset", "File offset"), new("length", "Bytes")],
                character.Ranges.Select(range => new TableRow($"slots/{slot.Index}/ranges/{range.Name}", new Dictionary<string, ViewNode>
                {
                    ["name"] = new TextNode(range.Name), ["offset"] = new TextNode($"0x{range.Offset:X}"),
                    ["length"] = new TextNode($"0x{range.Length:X}"),
                })).ToArray()) { Title = "Bounded semantic ranges", Description = "Event/acquisition ranges are candidates only: no quest, boss or completion claims. " + Provenance },
        ];
    }

    private IReadOnlyList<ViewNode> Equipment(SemanticCharacter character, Func<string, string, FieldSpec, Func<FieldValue>, FieldNode> bind)
    {
        var nodes = new List<ViewNode>
        {
            new TextNode("These related representations are shown independently. Ownership, active equipment and mirror dependencies are unproven."),
        };
        foreach (var range in character.Ranges.Where(range => range.Name is "EquipData" or "ChrAsm" or "ChrAsm2" or "EquippedItems"))
        {
            var rows = new List<TableRow>();
            var prefix = range.Name == "ChrAsm" ? EquipmentSlots.AssemblyPrefixWords : 0;
            for (var word = 0; word < range.Length / sizeof(uint); word++)
            {
                var offset = range.Offset + word * sizeof(uint);
                var label = EquipmentLabel(word, prefix);
                var id = $"slots/{character.Slot}/equipment/{range.Name}/{word}";
                var value = bind(id, label, IntegerSpec.UInt32 with { Format = IntegerFormat.Hex },
                    () => FieldValue.Of((long)EldenRingParser.ReadUInt32(bytes, offset))) with
                { Confidence = Confidence.Inferred, Description = Provenance };
                rows.Add(new TableRow(id, new Dictionary<string, ViewNode> { ["slot"] = new TextNode(label), ["raw"] = value }));
            }
            nodes.Add(new TableNode([new("slot", "Candidate slot"), new("raw", "Raw reference")], rows)
            { Title = range.Name, Description = Provenance });
        }
        return nodes;
    }

    private static string EquipmentLabel(int word, int prefix)
    {
        if (word < prefix) return word switch
        {
            0 => "Arm style", 1 => "Active left hand", 2 => "Active right hand", _ => $"Active projectile selector {word - 2}",
        };
        return EquipmentSlots.GearLabel(word - prefix) ?? $"Unknown word +0x{word * sizeof(uint):X}";
    }
}
