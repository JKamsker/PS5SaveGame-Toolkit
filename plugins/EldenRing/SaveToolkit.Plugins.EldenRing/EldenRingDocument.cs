using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing;

internal sealed class EldenRingDocument(string path, byte[] bytes, ParsedSave save) : BoundSaveDocument
{
    private readonly EldenRingHexExplorer _hex = new(bytes, save);

    public override string Title => "Elden Ring · PlayStation character slots";
    public override bool CanEdit => save.CanEdit;

    public override ViewNode BuildView() => new TabsNode(
    [
        new Tab("Profiles",
        [
            new StatsNode(
            [
                new Stat("Characters", $"{save.Slots.Count(slot => slot.Active)} / {EldenRingLayout.SlotCount}"),
                new Stat("Format", "PlayStation memory.dat"),
                new Stat("Editing", CanEdit ? "Held runes only" : "Read-only version"),
            ]),
            new TextNode(CanEdit
                ? "Experimental: held runes can be changed up to the existing lifetime rune total. Changes have not been tested in-game."
                : "This character/profile version has not been verified for writing. All fields are read-only.") { Tone = Tone.Warning },
            new TextNode("Slot indices are zero-based. Level and attributes are read-only: level = sum(attributes) − 79."),
            Profiles(),
        ]),
        new Tab("Characters", save.Slots.Select(slot => (ViewNode)SectionNode.Lazy(
            $"Slot {slot.Index}", $"characters/{slot.Index}", slot.Active ? "Active" : "Inactive")).ToArray()),
        new Tab("Raw", _hex.BuildSections()),
    ]);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        foreach (var slot in save.Slots)
        {
            if (lazyId == $"characters/{slot.Index}")
            {
                return CharacterFields(slot);
            }
        }

        return _hex.Expand(lazyId);
    }

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) => _hex.Read(blobId, offset, count);

    public override IReadOnlyList<FileOutput> Serialize()
    {
        // No PS per-slot MD5 exists. Recheck structure and profile mirrors before returning patched bytes.
        _ = EldenRingParser.Parse(bytes);
        return [new FileOutput(path, bytes.ToArray())];
    }

    private TableNode Profiles() => new(
    [
        new TableColumn("slot", "Slot") { Width = ColumnWidth.Narrow },
        new TableColumn("status", "Status"),
        new TableColumn("name", "Name"),
        new TableColumn("level", "Level"),
        new TableColumn("time", "Play time"),
    ], save.Slots.Select(slot => new TableRow($"slot/{slot.Index}", new Dictionary<string, ViewNode>
    {
        ["slot"] = new TextNode(slot.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ["status"] = new TextNode(slot.Active ? "Active" : "Inactive"),
        ["name"] = slot.Active ? Name(slot) : new TextNode("—"),
        ["level"] = slot.Active ? Level(slot) : new TextNode("—"),
        ["time"] = slot.Active ? PlayTime(slot) : new TextNode("—"),
    })).ToArray()) { Title = "Character profile summaries", Description = "Active names, levels, play time and rune history are cross-checked against character data." };

    private IReadOnlyList<ViewNode> CharacterFields(CharacterSlot slot)
    {
        if (!slot.Active)
        {
            return [new TextNode("Inactive profile. Any remaining bytes are preserved without interpreting deleted character data.")];
        }

        var player = slot.PlayerOffset!.Value;
        var memory = Read(player + EldenRingLayout.RuneMemoryOffset);
        var maximumRunes = Math.Min(memory, EldenRingLayout.MaximumRunes);
        var fields = new List<ViewNode>
        {
            Name(slot), Level(slot), PlayTime(slot),
            Field($"slots/{slot.Index}/runes", "Runes held", new IntegerSpec(0, maximumRunes),
                () => FieldValue.Of((long)Read(player + EldenRingLayout.RunesOffset)),
                CanEdit ? value => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(player + EldenRingLayout.RunesOffset), value.AsUInt32()) : null,
                value => value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > maximumRunes
                    ? $"Runes must be a whole number from 0 to {maximumRunes}." : null) with
                {
                    Confidence = Confidence.Verified,
                    Warning = "Limited to the existing lifetime rune total; lifetime history and its profile copy remain unchanged.",
                },
            ReadOnlyInteger($"slots/{slot.Index}/rune-memory", "Lifetime rune total", player + EldenRingLayout.RuneMemoryOffset),
            ReadOnlyInteger($"slots/{slot.Index}/version", "Character format version", slot.Offset, Confidence.Unknown),
        };
        string[] attributeNames = ["Vigor", "Mind", "Endurance", "Strength", "Dexterity", "Intelligence", "Faith", "Arcane"];
        for (var index = 0; index < attributeNames.Length; index++)
        {
            var name = attributeNames[index];
            fields.Add(ReadOnlyInteger($"slots/{slot.Index}/attributes/{name.ToLowerInvariant()}", name,
                player + EldenRingLayout.AttributesOffset + index * sizeof(uint)) with
                {
                    Description = "Read-only until class minima and dependent HP/FP/stamina updates are validated.",
                });
        }

        return fields;
    }

    private FieldNode Name(CharacterSlot slot) => Field($"slots/{slot.Index}/name", "Name",
        new TextSpec { MaxLength = EldenRingLayout.NameCharacters },
        () => FieldValue.Of(EldenRingParser.ReadName(bytes, slot.ProfileOffset))) with { Confidence = Confidence.Verified };

    private FieldNode Level(CharacterSlot slot) => ReadOnlyInteger($"slots/{slot.Index}/level", "Level",
        slot.PlayerOffset!.Value + EldenRingLayout.LevelOffset) with { Description = "Sum of the eight attributes minus 79; matched to the profile summary." };

    private FieldNode PlayTime(CharacterSlot slot) => Field($"slots/{slot.Index}/playtime", "Play time",
        IntegerSpec.UInt64 with { Format = IntegerFormat.Duration, UnitsPerSecond = (long)EldenRingLayout.MillisecondsPerSecond },
        () => FieldValue.Of(EldenRingParser.ReadUInt64(bytes, slot.Offset + EldenRingLayout.SlotPlayTimeOffset))) with
        {
            Confidence = Confidence.Verified,
            Description = "Milliseconds in character data; whole seconds are cross-checked against the profile summary.",
        };

    private FieldNode ReadOnlyInteger(string id, string label, int offset, Confidence confidence = Confidence.Verified) =>
        Field(id, label, IntegerSpec.UInt32, () => FieldValue.Of((long)Read(offset))) with { Confidence = confidence };

    private uint Read(int offset) => EldenRingParser.ReadUInt32(bytes, offset);
}
