using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise;

internal sealed class AriseDocument(string path, string fileName, AriseSave save, IReferenceContext references) : BoundSaveDocument, IQueryableDocument
{
    private const int GaldOffset = 0x18;
    private const int PlaySecondsOffset = 0xAF8;
    private const int PlayTicksOffset = 0x2000;
    private const int LevelOffset = 0x04;
    private const int CurrentHpOffset = 0x08;
    private const int ExperienceOffset = 0x10;
    private const int SkillPointsOffset = 0x30;
    private const int BaseHpOffset = 0x50;
    private const int ItemFlagsOffset = 1;
    private const int ItemIdOffset = 2;
    private const uint MaximumLevel = 100;
    private const uint MaximumHp = 99999;
    private const uint MaximumSkillPoints = 999999;
    private const uint MaximumGald = 999999999;
    private const int MainCharacterCount = 6;
    private const int ItemRecordSize = 16;
    private const int ItemRecordCount = 0x3A6; // Count in the cited PS4 inventory patch; later regions are not item stacks.
    private const int ConsumableStackLimit = 15;
    private const string ExperimentalWarning = "PS5 field mapping is inferred from PS4 research. Edits have not been tested in-game.";
    private readonly AriseReferences _references = new(references);

    public override string Title => $"Tales of Arise · {fileName}";

    public override ViewNode BuildView()
    {
        var party = Section("PartyProfile");
        return new TabsNode(
        [
            new Tab("Party",
            [
                new TextNode(ExperimentalWarning) { Tone = Tone.Warning },
                new StatsNode(
                [
                    new Stat("Gald (inferred)", Read(party.Offset + GaldOffset).ToString()),
                    new Stat("Play time (inferred)", TimeSpan.FromSeconds(Read(party.Offset + PlaySecondsOffset)).ToString()),
                    new Stat("Integrity", "Ciphertext SHA1 verified · 30 linked sections"),
                ]),
                Number("party/gald", "Gald", party.Offset + GaldOffset, MaximumGald),
                Number("party/play-seconds", "Play time (seconds)", party.Offset + PlaySecondsOffset, uint.MaxValue, writable: false)
                    with { Spec = IntegerSpec.UInt32 with { ReadOnly = true, Format = IntegerFormat.Duration, UnitsPerSecond = 1 } },
                Number("party/play-ticks", "Play time (60 ticks/second, inferred)", party.Offset + PlayTicksOffset, uint.MaxValue, writable: false),
                Characters(),
            ]),
            new Tab("Inventory",
            [
                new TextNode("Only existing, recognised consumables can be edited (0–15). Equipment and unknown IDs are read-only. Item flags, IDs and order are preserved.")
                    { Tone = Tone.Warning },
                SectionNode.Lazy("Owned inventory records", "inventory"),
            ]),
            new Tab("Catalogs", _references.BuildView()),
            new Tab("Explorer",
            [
                new TextNode("Decrypted sections and primitive words are read-only. Offsets are relative to the gameplay block at file offset 0x32058."),
                .. save.Sections.Select((section, index) => SectionNode.Lazy(section.Name, $"section/{index}", $"0x{section.Size:X} bytes")),
                new HexNode("payload", save.Payload.Length) { Title = "Decrypted gameplay block (paged)" },
                new HexNode("outer", AriseFormat.PayloadOffset) { Title = "Preserved outer header and screenshot (paged)" },
            ]),
        ]);
    }

    public IReadOnlyList<QueryableCollection> Collections => _references.Collections;

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) => _references.Query(collectionId, query);

    public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
        _references.Handles(actionId) ? _references.Inspect(actionId, parameters) : base.InvokeAction(actionId, parameters);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        if (lazyId == "inventory")
        {
            return [Inventory()];
        }

        return AriseExplorer.Expand(lazyId, save, offset => Number($"raw/{offset:X}", $"0x{offset:X}", offset, uint.MaxValue, false)
            with { Confidence = Confidence.Unknown, Warning = null });
    }

    public override IReadOnlyList<FileOutput> Serialize() => [new FileOutput(path, AriseFormat.Encode(save))];

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        var bytes = blobId switch
        {
            "payload" => save.Payload.AsMemory(),
            "outer" => save.Original.AsMemory(0, AriseFormat.PayloadOffset),
            _ => throw new KeyNotFoundException($"Unknown blob '{blobId}'."),
        };
        if (offset < 0 || offset > bytes.Length || count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        // Return an isolated page; callers cannot mutate the document's backing storage.
        return bytes.Slice((int)offset, Math.Min(count, bytes.Length - (int)offset)).ToArray();
    }

    private TableNode Characters()
    {
        var names = AriseSchema.CharacterNames;
        var rows = new List<TableRow>();
        for (var i = 0; i < names.Count; i++)
        {
            var section = Section($"ArisePCStatus_{i:000}");
            var offset = section.Offset;
            var active = i < MainCharacterCount && Read(offset + LevelOffset) != 0;
            var prefix = $"character/{i}";
            rows.Add(new TableRow(prefix, new Dictionary<string, ViewNode>
            {
                ["name"] = new TextNode(names[i]),
                ["level"] = Number($"{prefix}/level", "Level", offset + LevelOffset, MaximumLevel, active, active ? 1u : 0u)
                    with { Warning = "Level alone does not recalculate experience or stats. The preserved load-screen preview can show the old level." },
                ["hp"] = Number($"{prefix}/hp", "Current HP", offset + CurrentHpOffset, MaximumHp, active),
                ["baseHp"] = Number($"{prefix}/base-hp", "Base HP", offset + BaseHpOffset, uint.MaxValue, false),
                ["experience"] = Number($"{prefix}/experience", "Experience", offset + ExperienceOffset, uint.MaxValue, false),
                ["sp"] = Number($"{prefix}/sp", "SP", offset + SkillPointsOffset, MaximumSkillPoints, active),
            }));
        }

        return new TableNode(
        [
            new("name", "Character"), new("level", "Level"), new("hp", "HP"),
            new("baseHp", "Base HP"), new("experience", "Experience"), new("sp", "SP"),
        ], rows) { Title = "Characters (PS5 mapping inferred)" };
    }

    private TableNode Inventory()
    {
        var section = Section("SaveData_ItemManager");
        var rows = new List<TableRow>();
        for (var relative = 0; relative < ItemRecordCount * ItemRecordSize; relative += ItemRecordSize)
        {
            var offset = section.Offset + relative;
            var id = BinaryPrimitives.ReadUInt16LittleEndian(save.Payload.AsSpan(offset + ItemIdOffset));
            if (id == 0)
            {
                continue;
            }

            var name = AriseSchema.ConsumableName(id);
            var valueId = $"inventory/{relative / ItemRecordSize}/quantity";
            var idField = Field($"inventory/{relative / ItemRecordSize}/id", $"0x{id:X4}",
                new ReferenceSpec(AriseReferences.ItemsCatalogId) { ReadOnly = true, AllowCustom = true },
                () => FieldValue.Of(id.ToString())) with
                {
                    Confidence = Confidence.Inferred,
                    Description = name is null
                        ? "Unknown item/equipment identity; raw numeric ID is preserved."
                        : $"{name}; raw numeric ID is preserved. Catalog attribution is shown in the Catalogs tab.",
                };
            var spec = new IntegerSpec(0, Math.Max(ConsumableStackLimit, (int)save.Payload[offset]));
            var canWrite = name is not null && save.Payload[offset] <= ConsumableStackLimit;
            var quantity = Field(valueId, "Quantity", spec,
                () => FieldValue.Of((long)save.Payload[offset]),
                canWrite ? v => save.Payload[offset] = checked((byte)v.AsUInt32()) : null,
                v => v.AsInteger() < 0 || v.AsInteger() > ConsumableStackLimit ? "Consumable quantities must be between 0 and 15." : null)
                with { Confidence = Confidence.Inferred, Warning = canWrite ? ExperimentalWarning : null };
            rows.Add(new TableRow(valueId, new Dictionary<string, ViewNode>
            {
                ["id"] = idField,
                ["name"] = new TextNode(name ?? "Unknown item / equipment"),
                ["quantity"] = quantity,
                ["flags"] = new TextNode($"0x{save.Payload[offset + ItemFlagsOffset]:X2}"),
            }));
        }

        return new TableNode([new("id", "ID"), new("name", "Item"), new("quantity", "Quantity"), new("flags", "Raw flags")], rows);
    }

    private FieldNode Number(string id, string label, int offset, uint maximum, bool writable = true, uint minimum = 0)
    {
        var current = Read(offset);
        writable = writable && current >= minimum && current <= maximum;
        var spec = new IntegerSpec(Math.Min(minimum, current), Math.Max(maximum, current));
        return Field(id, label, spec, () => FieldValue.Of((long)Read(offset)),
            writable ? v => BinaryPrimitives.WriteUInt32LittleEndian(save.Payload.AsSpan(offset), v.AsUInt32()) : null,
            v => v.AsInteger() < spec.Min || v.AsInteger() > spec.Max ? "Value is outside the supported range." : null)
            with { Confidence = Confidence.Inferred, Warning = writable ? ExperimentalWarning : null };
    }

    private uint Read(int offset) => AriseFormat.U32(save.Payload, offset);
    private AriseSection Section(string name) => save.Sections.Single(section => section.Name == name);
}
