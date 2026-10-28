using System.Buffers.Binary;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal delegate FieldNode FaithReadField(string id, string label, FieldSpec spec,
    Func<FieldValue> get, Confidence confidence, string description);

// Wire offsets adapted from Nenkai's MIT FaithSaveFile_Sub.bt; see THIRD_PARTY_NOTICES.md.
// No setter exists for any of these fields. Array indices are never promoted to skill IDs.
internal sealed class FaithReadOnlyContext(byte[] bytes, FaithContainer container, FaithSchema schema, IReferenceContext references)
{
    private const string SchemaSource = "Inferred from Nenkai/010GameTemplates FaithSaveFile_Sub.bt (MIT), " +
        "revision 4a3685f9b63f72ca49e24ac2f18420aaad2762b8; dimensions match PS5 samples. " +
        "https://github.com/Nenkai/010GameTemplates/blob/4a3685f9b63f72ca49e24ac2f18420aaad2762b8/" +
        "Square%20Enix/Final%20Fantasy%2016/FaithSaveFile_Sub.bt. Gameplay semantics need controlled captures.";
    private const int EquipmentBanks = 3;
    private const int EquipmentSlots = 6;
    private const int SkillRanks = 68;
    private const int FlagBytes = 12;
    private const int ProtectOffset = SkillRanks + FlagBytes;
    private const int MapOffset = 4;
    private const int PartyIdOffset = 16;
    private const int PartyCountOffset = 20;
    private const int SummonModesOffset = 40;
    private const int SummonCount = 3;
    private const int CommandsOffset = 52;
    private const int CommandCount = 22;
    private const int ShortcutOffset = 140;
    private const int ShortcutCount = 4;
    private const int HpOffset = 16;
    private const int RecoveryOffset = 20;
    private const int LimitBreakOffset = 24;
    private const int CurrentSummonOffset = 28;
    private const int LearnedSummonsOffset = 32;
    private const int RevealedSummonsOffset = 36;

    public IReadOnlyList<ViewNode> Build(FaithReadField field, Func<string, int, FieldNode> item) =>
    [
        new TextNode("Read-only research context. Bank activation, slot ordering, skill-index mapping, Eikon flags and XP thresholds " +
            "have not been proven by controlled PS5 captures. Nothing here enables progression or loadout writes.") { Tone = Tone.Warning },
        Equipment(item), Party(field), Skills(field), Player(field, item),
    ];

    private SectionNode Equipment(Func<string, int, FieldNode> item)
    {
        var block = FaithSchema.Unique(container, FaithSchema.EquipmentType, FaithSchema.SupportsEquipment);
        if (block is null) return Missing("Equipment banks", FaithSchema.EquipmentType);
        string[] names = ["itemArray (candidate adult bank)", "itemArrayBoyhood (candidate boyhood bank)", "virtualEquip (virtual bank)"];
        var nodes = new List<ViewNode>
        {
            new TextNode(SchemaSource) { Tone = Tone.Muted },
            new TextNode("These are three distinct stored arrays, not three confirmed current loadouts. " +
                "Slot numbers follow serialized order; 900000 remains an unknown ID.") { Tone = Tone.Warning },
        };
        for (var bank = 0; bank < EquipmentBanks; bank++)
        {
            var bankIndex = bank;
            nodes.Add(new TableNode([new TableColumn("slot", "Serialized index"), new TableColumn("item", "Raw ID / equipment")],
                Enumerable.Range(0, EquipmentSlots).Select(slot => new TableRow($"equipment/{bankIndex}/{slot}",
                    new Dictionary<string, ViewNode>
                    {
                        ["slot"] = new TextNode(FaithCatalog.Key(slot)),
                        ["item"] = item($"equipment/{bankIndex}/{slot}/item",
                            Int(block.PayloadOffset + (bankIndex * EquipmentSlots + slot) * sizeof(int))),
                    })).ToArray()) { Title = names[bank] });
        }
        return new SectionNode("Equipment banks — Inferred", nodes);
    }

    private SectionNode Party(FaithReadField field)
    {
        var block = FaithSchema.Unique(container, FaithSchema.PartyType, FaithSchema.SupportsParty);
        if (block is null) return Missing("Location / party", FaithSchema.PartyType);
        var offset = block.PayloadOffset;
        var map = Int(offset + MapOffset);
        var nodes = new List<ViewNode>
        {
            Number(field, "party/scene", "Candidate scene ID", () => Int(offset)),
            Reference(field, "party/map", "Candidate map", map, FaithCatalog.MapsId),
            Number(field, "party/id", "Candidate party ID", () => Int(offset + PartyIdOffset)),
            Number(field, "party/count", "Stored member count", () => Int(offset + PartyCountOffset)),
            new TextNode("A map ID alone does not identify a single asset/sky variant. No teleport or coordinate edits are available.") { Tone = Tone.Muted },
        };
        var record = references.Catalog(FaithCatalog.MapsId)?.Find(FaithCatalog.Key(map));
        if (record is not null)
        {
            foreach (var attribute in record.Attributes)
                nodes.Add(new TextNode($"{attribute.Key}: {FaithCatalog.PlainText(attribute.Value)}. {record.AttributionText}"));
        }
        return new SectionNode("Location / party — Inferred", nodes);
    }

    private SectionNode Skills(FaithReadField field)
    {
        var block = FaithSchema.Unique(container, FaithSchema.SkillType, FaithSchema.SupportsSkills);
        if (block is null) return Missing("Ability arrays", FaithSchema.SkillType);
        return new SectionNode("Ability arrays — index meanings Unknown", [
            new TextNode("skillLv has 68 bytes. newFlags and protectFlags are separate 96-bit arrays. " +
                "Byte index is not a demonstrated skill ID; observed rank ranges are not legal write bounds. " + SchemaSource),
            ByteTable(field, block.PayloadOffset, SkillRanks, "skills/ranks", "Stored rank bytes"),
            ByteTable(field, block.PayloadOffset + SkillRanks, FlagBytes, "skills/new", "Raw newFlags bytes (all 96 bits)"),
            ByteTable(field, block.PayloadOffset + ProtectOffset, FlagBytes, "skills/protect", "Raw protectFlags bytes (all 96 bits)"),
        ]);
    }

    private SectionNode Player(FaithReadField field, Func<string, int, FieldNode> item)
    {
        var block = FaithSchema.Unique(container, FaithSchema.PlayerType, schema.SupportsPlayer);
        if (block is null) return Missing("Growth / Eikon context", FaithSchema.PlayerType);
        var offset = block.PayloadOffset;
        var nodes = new List<ViewNode>
        {
            new TextNode("The 272-byte common player prefix is decoded independently of the extended palette suffix. " +
                "Level/XP remain on Overview. XP may be within-level or cumulative; growth thresholds, effective caps and HP recomputation are unproven."),
            Number(field, "context/hp", "Stored HP", () => Int(offset + HpOffset)),
            Number(field, "context/recovery", "Stored recovery", () => Int(offset + RecoveryOffset)),
            Number(field, "context/lb", "Stored limit-break value", () => Int(offset + LimitBreakOffset)),
            Number(field, "context/currentSummon", "Stored summon index", () => Int(offset + CurrentSummonOffset)),
            Number(field, "context/learnedSummons", "Raw learned-summon flags", () => Int(offset + LearnedSummonsOffset)),
            Number(field, "context/revealedSummons", "Raw revealed-summon flags", () => Int(offset + RevealedSummonsOffset)),
        };
        for (var index = 0; index < SummonCount; index++)
            nodes.Add(Reference(field, $"context/summons/{index}", $"Summon slot {index}",
                Int(offset + SummonModesOffset + index * sizeof(int)), FaithCatalog.SummonsId));
        nodes.Add(new SectionNode("Stored command IDs (not skill ranks)", Enumerable.Range(0, CommandCount)
            .Select(index => (ViewNode)Reference(field, $"context/commands/{index}", $"Command {index}",
                Int(offset + CommandsOffset + index * sizeof(int)), FaithCatalog.CommandsId)).ToArray()));
        nodes.Add(new SectionNode("Stored item shortcuts", Enumerable.Range(0, ShortcutCount)
            .Select(index => (ViewNode)item($"context/shortcuts/{index}", Int(offset + ShortcutOffset + index * sizeof(int)))).ToArray()));
        var growth = references.Catalog(FaithCatalog.GrowthId)?.Find(FaithCatalog.Key(Int(offset)));
        nodes.Add(new TextNode(growth is null ? "No matched growth dataset is available. No XP target, level cap or projected stats are calculated." :
            $"Optional reference context: {FaithCatalog.PlainText(growth.Name)}. {growth.AttributionText}. " +
            "This label does not prove the save's growth-curve variant or XP semantics."));
        nodes.Add(new TextNode(block.PayloadLength == FaithSchema.ExtendedPlayerLength
            ? "Extended palette suffix: 1264 opaque bytes preserved separately; no palette allocator interpretation or edits."
            : "Legacy player schema: no extended palette suffix."));
        return new SectionNode("Growth / Eikon context — Inferred", nodes);
    }

    private TableNode ByteTable(FaithReadField field, int offset, int count, string id, string title) =>
        new([new TableColumn("index", "Byte index"), new TableColumn("value", "Raw byte")],
            Enumerable.Range(0, count).Select(index => new TableRow($"{id}/{index}", new Dictionary<string, ViewNode>
            {
                ["index"] = new TextNode(FaithCatalog.Key(index)),
                ["value"] = field($"{id}/{index}", $"Byte {index}", IntegerSpec.UInt8,
                    () => FieldValue.Of((int)bytes[offset + index]), Confidence.Unknown, SchemaSource),
            })).ToArray()) { Title = title };

    private FieldNode Reference(FaithReadField field, string id, string label, int value, string catalogId)
    {
        var record = references.Catalog(catalogId)?.Find(FaithCatalog.Key(value));
        return field(id, record is null ? label : $"{label}: {FaithCatalog.PlainText(record.Name)}",
            new ReferenceSpec(catalogId) { AllowCustom = true }, () => FieldValue.Of(FaithCatalog.Key(value)),
            Confidence.Inferred, SchemaSource + " " + (record?.AttributionText ?? "No matched name dataset; raw ID retained."));
    }

    private static FieldNode Number(FaithReadField field, string id, string label, Func<int> get) =>
        field(id, label, IntegerSpec.Int32, () => FieldValue.Of(get()), Confidence.Inferred, SchemaSource);

    private static SectionNode Missing(string name, uint type) => new(name,
        [new TextNode($"Type {type}: supported unique block not found. Unknown version/length, duplicate and absent blocks stay in Structure/Hex.")]);

    private int Int(int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
}
