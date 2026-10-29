using System.Buffers.Binary;
using static SaveToolkit.Plugins.FinalFantasy16.FaithContextLayout;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal delegate FieldNode FaithContextField(string id, string label, FieldSpec spec,
    Func<FieldValue> get, Confidence confidence, string description);

// Wire offsets adapted from Nenkai's MIT FaithSaveFile_Sub.bt; see THIRD_PARTY_NOTICES.md.
// FaithDocument supplies gated fixed-width bindings. Array indices are never promoted to skill IDs.
internal sealed class FaithCharacterContext(byte[] bytes, FaithContainer container, FaithSchema schema, IReferenceContext references)
{
    internal const string SchemaSource = "Inferred from Nenkai/010GameTemplates FaithSaveFile_Sub.bt (MIT), " +
        "revision 4a3685f9b63f72ca49e24ac2f18420aaad2762b8; dimensions match PS5 samples. " +
        "https://github.com/Nenkai/010GameTemplates/blob/4a3685f9b63f72ca49e24ac2f18420aaad2762b8/" +
        "Square%20Enix/Final%20Fantasy%2016/FaithSaveFile_Sub.bt. Gameplay semantics need controlled captures.";
    private readonly FaithCatalog _catalog = new(references);

    public IReadOnlyList<ViewNode> Build(FaithContextField field, Func<string, int, FieldNode> item) =>
    [
        new TextNode(FaithWriteMap.Warning + "Bank activation, slot ordering, skill-index mapping, Eikon flags and XP thresholds " +
            "have not been proven by controlled PS5 captures. Raw writes do not establish a legal progression or loadout.") { Tone = Tone.Warning },
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

    private SectionNode Party(FaithContextField field)
    {
        var block = FaithSchema.Unique(container, FaithSchema.PartyType, FaithSchema.SupportsParty);
        if (block is null) return Missing("Location / party", FaithSchema.PartyType);
        var offset = block.PayloadOffset;
        var map = Int(offset + MapOffset);
        var nodes = new List<ViewNode>
        {
            Number(field, "party/scene", "Candidate scene ID", () => Int(offset)),
            Reference(field, "party/map", "Candidate map", () => Int(offset + MapOffset), FaithCatalog.MapsId),
            Number(field, "party/id", "Candidate party ID", () => Int(offset + PartyIdOffset)),
            Number(field, "party/count", "Stored member count", () => Int(offset + PartyCountOffset)),
            new TextNode("A map ID alone does not identify a single asset/sky variant. Changing IDs does not relocate member transforms, " +
                "reconcile quests or constitute a validated teleport.") { Tone = Tone.Warning },
        };
        if (FaithLocationView.Build(bytes, container, _catalog, references) is { } location)
        {
            nodes.Add(location);
            nodes.Add(SectionNode.Lazy("Regional map artwork (spoilers)", FaithMapArt.RegionalLazyId,
                "Four reference regions; no save-to-image coordinate mapping"));
        }
        var record = _catalog.Find(FaithCatalog.MapsId, map);
        if (record is not null)
        {
            foreach (var attribute in record.Attributes)
                nodes.Add(new TextNode($"{attribute.Key}: {FaithCatalog.PlainText(attribute.Value)}. {record.AttributionText}"));
        }
        return new SectionNode("Location / party — Inferred", nodes);
    }

    private SectionNode Skills(FaithContextField field)
    {
        var block = FaithSchema.Unique(container, FaithSchema.SkillType, FaithSchema.SupportsSkills);
        if (block is null) return Missing("Ability arrays", FaithSchema.SkillType);
        return new SectionNode("Ability arrays — index meanings Unknown", [
            new TextNode("skillLv has 68 bytes. newFlags and protectFlags are separate 96-bit arrays. " +
                "Byte index is not a demonstrated skill ID; observed rank ranges are conservative editing bounds, not proven legal ranks. " + SchemaSource),
            new SectionNode("Named Nex skill IDs — save-rank association Unknown", [
                new TextNode("These reference IDs are named by upstream research. They are not joined to the rank bytes below, " +
                    "and do not report learned, mastered or unlocked state. At most 68 reference rows are shown."),
                new TableNode([new TableColumn("id", "Nex skill ID"), new TableColumn("name", "Research name / provenance")],
                    _catalog.SkillReferences().Select(record => new TableRow($"skills/references/{record.Key}", new Dictionary<string, ViewNode>
                    {
                        ["id"] = new TextNode(record.Key),
                        ["name"] = field($"skills/references/{record.Key}", FaithCatalog.PlainText(record.Name),
                            new ReferenceSpec(FaithCatalog.SkillsId) { AllowCustom = true }, () => FieldValue.Of(record.Key),
                            Confidence.Inferred, FaithCatalog.Describe(record)),
                    })).ToArray()),
            ]),
            ByteTable(field, block.PayloadOffset, SkillRanks, "skills/ranks", "Stored rank bytes"),
            ByteTable(field, block.PayloadOffset + SkillRanks, FlagBytes, "skills/new", "Raw newFlags bytes (all 96 bits)"),
            ByteTable(field, block.PayloadOffset + ProtectOffset, FlagBytes, "skills/protect", "Raw protectFlags bytes (all 96 bits)"),
        ]);
    }

    private SectionNode Player(FaithContextField field, Func<string, int, FieldNode> item)
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
        {
            var summonOffset = offset + SummonModesOffset + index * sizeof(int);
            nodes.Add(Reference(field, $"context/summons/{index}", $"Summon slot {index}",
                () => Int(summonOffset), FaithCatalog.SummonsId));
            nodes.Add(Reference(field, $"context/feats/{index}", $"Reference feat for summon slot {index}",
                () => Int(summonOffset), FaithCatalog.FeatsId));
        }
        nodes.Add(new TextNode("Reference feats describe a published vanilla summon/command relationship. " +
            "They are not stored command selections or proof of learned ranks, Eikon unlocks, mastery or active loadout."));
        nodes.Add(new SectionNode("Stored command IDs (not skill ranks)", Enumerable.Range(0, CommandCount)
            .Select(index => (ViewNode)Reference(field, $"context/commands/{index}", $"Command {index}",
                () => Int(offset + CommandsOffset + index * sizeof(int)), FaithCatalog.CommandsId)).ToArray()));
        nodes.Add(new SectionNode("Stored item shortcuts", Enumerable.Range(0, ShortcutCount)
            .Select(index => (ViewNode)item($"context/shortcuts/{index}", Int(offset + ShortcutOffset + index * sizeof(int)))).ToArray()));
        nodes.Add(new FaithGrowthContext(bytes, block).Build(field));
        var growth = _catalog.Find(FaithCatalog.GrowthId, Int(offset));
        nodes.Add(new TextNode(growth is null ? "No matched growth dataset is available. No XP target, level cap or projected stats are calculated." :
            $"Optional reference context: {FaithCatalog.PlainText(growth.Name)}. {growth.AttributionText}. " +
            "This label does not prove the save's growth-curve variant or XP semantics."));
        nodes.Add(new TextNode(block.PayloadLength == FaithSchema.ExtendedPlayerLength
            ? "Extended palette suffix: 1264 opaque bytes preserved separately; no palette allocator interpretation or edits."
            : "Legacy player schema: no extended palette suffix."));
        return new SectionNode("Growth / Eikon context — Inferred", nodes);
    }

    private TableNode ByteTable(FaithContextField field, int offset, int count, string id, string title) =>
        new([new TableColumn("index", "Byte index"), new TableColumn("value", "Raw byte")],
            Enumerable.Range(0, count).Select(index => new TableRow($"{id}/{index}", new Dictionary<string, ViewNode>
            {
                ["index"] = new TextNode(FaithCatalog.Key(index)),
                ["value"] = field($"{id}/{index}", $"Byte {index}", IntegerSpec.UInt8,
                    () => FieldValue.Of((int)bytes[offset + index]), Confidence.Unknown, SchemaSource),
            })).ToArray()) { Title = title };

    private FieldNode Reference(FaithContextField field, string id, string label, Func<int> get, string catalogId)
    {
        var value = get();
        var record = _catalog.Find(catalogId, value);
        return field(id, record is null ? label : $"{label}: {FaithCatalog.PlainText(record.Name)}",
            new ReferenceSpec(catalogId) { AllowCustom = true }, () => FieldValue.Of(FaithCatalog.Key(get())),
            record is null ? Confidence.Unknown : Confidence.Inferred,
            SchemaSource + " " + (record is null ? "No matched name dataset; raw ID retained." : FaithCatalog.Describe(record)));
    }

    private static FieldNode Number(FaithContextField field, string id, string label, Func<int> get) =>
        field(id, label, IntegerSpec.Int32, () => FieldValue.Of(get()), Confidence.Inferred, SchemaSource);

    private static SectionNode Missing(string name, uint type) => new(name,
        [new TextNode($"Type {type}: supported unique block not found. Unknown version/length, duplicate and absent blocks stay in Structure/Hex.")]);

    private int Int(int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
}
