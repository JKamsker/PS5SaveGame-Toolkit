using System.Globalization;
using System.Numerics;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed class RiftPlayerView
{
    internal const uint CampaignTag = 0xD436_A987;
    internal const uint GoldBoltTag = 0x77F0_0BE8;
    internal const uint SpeedrunTag = 0xA787_E610;
    private const string DefinitionsResource = "SaveToolkit.Plugins.Insomniac.Resources.PlayerFields.json";
    private const string GoldBoltOptionsGroup = "Gold bolt options";
    private const int SecondsPerHour = 3600;
    private readonly Dictionary<uint, IReadOnlyDictionary<string, DdlField>> _sections = [];
    private readonly IReadOnlyList<PlayerFieldDefinition> _definitions;

    internal RiftPlayerView(Dat1Container container)
    {
        using var stream = typeof(RiftPlayerView).Assembly.GetManifestResourceStream(DefinitionsResource)!;
        _definitions = JsonSerializer.Deserialize<PlayerFieldDefinition[]>(stream)!;
        foreach (var section in container.Sections.Where(s => s.Tag is CampaignTag or GoldBoltTag or SpeedrunTag))
        {
            if (!_sections.TryAdd(section.Tag, new DdlReader(container, section).Read()))
                throw new SaveFormatException("Duplicate Rift Apart player section.");
        }
        ValidateKnownFields();
    }

    internal bool HasCampaign => _sections.ContainsKey(CampaignTag);

    internal IReadOnlyList<ViewNode> Build(Func<string, string, FieldSpec, FieldValue, FieldNode> bind)
    {
        var result = new List<ViewNode>
        {
            new TextNode("Read-only: gameplay meanings are substantiated, but save integrity and console loading after edits are unverified.")
                { Tone = Tone.Warning },
        };
        if (!HasCampaign) return result;

        var campaign = _sections[CampaignTag];
        var bolts = campaign["CareerBolts"].Values[0].AsInteger() - campaign["SpentBolts"].Values[0].AsInteger();
        var playtime = campaign["Playtime"].Values[0].AsDouble();
        result.Add(new StatsNode([
            new Stat("Bolts (calculated)", bolts.ToString(CultureInfo.InvariantCulture)) { Hint = "Inferred: CareerBolts minus SpentBolts." },
            new Stat("Play time (hours)", (playtime / SecondsPerHour).ToString("0.00", CultureInfo.InvariantCulture))
                { Hint = "Seconds are inferred from the timer scale and snapshot progression." },
        ]));
        result.Add(bind("player/AvailableBolts", "Available bolts (calculated)",
            new IntegerSpec(-(Int128)ulong.MaxValue, ulong.MaxValue), FieldValue.Of(bolts)) with
        {
            Confidence = Confidence.Inferred,
            Description = "CareerBolts minus SpentBolts; not independently confirmed against an in-game display.",
        });

        foreach (var group in _definitions.GroupBy(d => d.Group))
        {
            var fields = group.Select(definition => BuildField(definition, bind)).OfType<ViewNode>().ToArray();
            if (fields.Length > 0) result.Add(new SectionNode(group.Key, fields));
        }

        result.Add(new SectionNode("Collectibles", Collectibles(bind)));
        result.Add(LevelTable());
        result.Add(new TextNode("Health/armor upgrades, weapon ammunition and individual weapon levels remain unidentified. The INVT payload is still opaque. Career weapon XP is an aggregate, and CurrentLevel is a location code.")
            { Tone = Tone.Info });
        return result;
    }

    internal IReadOnlyList<ViewNode> RawFields(uint tag, Func<string, string, FieldSpec, FieldValue, FieldNode> bind)
    {
        if (!_sections.TryGetValue(tag, out var fields)) return [];
        var result = new List<ViewNode>();
        foreach (var field in fields.Values)
        {
            var children = field.Values.Select((value, index) => (ViewNode)(bind(
                $"ddl/{tag:x8}/{field.Name}/{index}", field.Count == 1 ? field.Name : $"{field.Name}[{index}]",
                field.Spec, value) with
                {
                    Confidence = Confidence.Unknown,
                    Description = $"Stored name hash validated; type {field.Kind}; field starts at 0x{field.Offset:X}. Meaning is not asserted by this raw view.",
                })).ToArray();
            result.Add(new SectionNode(field.Name, children)
            {
                Badge = $"{field.Kind} · {field.Count}",
                Description = field.Kind == DdlValueKind.Object ? "Nested objects are retained opaque; inspect the section hex." : null,
                Collapsed = field.Count != 1,
            });
        }
        return result;
    }

    private FieldNode? BuildField(PlayerFieldDefinition definition, Func<string, string, FieldSpec, FieldValue, FieldNode> bind)
    {
        var tag = definition.Group == GoldBoltOptionsGroup ? GoldBoltTag : CampaignTag;
        if (!_sections.TryGetValue(tag, out var fields)) return null;
        var field = fields[definition.Name];
        var description = $"Stored field {field.Name}; name hash matches; serialized type {field.Kind}.";
        var warning = definition.Name switch
        {
            "CareerRaritanium" or "CareerRaritaniumSpend" => "These are separate stored counters. Spending exceeds the career value in late samples; an available balance is not established.",
            "CareerExp" => "XP is verified; the health/level conversion is unknown.",
            "CareerWeaponExp" => "Aggregate career weapon XP; individual weapon XP and levels are unknown.",
            "Difficulty" => "Numeric code only; difficulty names are not confirmed for this PS5 version.",
            "Playtime" => "The field name is verified. Seconds as its unit are inferred, and float32 precision is retained.",
            _ => null,
        };
        var spec = definition.Name == "RYNOPlansCollected" && field.Spec is IntegerSpec integer
            ? integer with { Format = IntegerFormat.Hex } : field.Spec;
        return bind($"player/{field.Name}", definition.Label, spec, field.Values[0]) with
        {
            Confidence = Confidence.Verified,
            Description = description,
            Warning = warning,
        };
    }

    private IReadOnlyList<ViewNode> Collectibles(Func<string, string, FieldSpec, FieldValue, FieldNode> bind)
    {
        var result = new List<ViewNode>();
        if (_sections.TryGetValue(GoldBoltTag, out var fields) && fields.TryGetValue("CollectedBoltHashes", out var hashes))
        {
            result.Add(bind("player/GoldBoltCount", "Collected gold bolts", IntegerSpec.UInt32, FieldValue.Of((long)hashes.Count)) with
            {
                Confidence = Confidence.Verified,
                Description = "Number of entries in CollectedBoltHashes, including zero for the null/empty array encoding.",
            });
        }
        var mask = _sections[CampaignTag]["RYNOPlansCollected"].Values[0].AsUInt64();
        result.Add(bind("player/SpybotCount", "Collected spybots (calculated)", IntegerSpec.UInt8,
            FieldValue.Of((long)BitOperations.PopCount(mask))) with
        {
            Confidence = Confidence.Inferred,
            Description = "Population count of RYNOPlansCollected; bit-to-location mapping is unknown.",
        });
        return result;
    }

    private TableNode LevelTable()
    {
        string[] names = ["LevelBolts", "LevelExp", "LevelWeaponExp", "LevelRaritanium", "LevelGoldBolts", "LevelRYNOPlans"];
        var campaign = _sections[CampaignTag];
        var count = names.Where(campaign.ContainsKey).Select(name => campaign[name].Count).DefaultIfEmpty().Max();
        var rows = Enumerable.Range(0, count).Select(index =>
        {
            var cells = new Dictionary<string, ViewNode> { ["index"] = new TextNode(index.ToString(CultureInfo.InvariantCulture)) };
            foreach (var name in names)
                cells[name] = new TextNode(campaign.TryGetValue(name, out var field) && index < field.Values.Count
                    ? field.Values[index].ToString() : "—");
            return new TableRow(index.ToString(CultureInfo.InvariantCulture), cells);
        }).ToArray();
        return new TableNode([new("index", "Location index"), .. names.Select(name => new TableColumn(name, name))], rows)
        {
            Title = "Per-location stored counters",
            Description = "Names and array indices are verified; planet names are unknown. Arrays need not equal lifetime collectible totals.",
        };
    }

    private void ValidateKnownFields()
    {
        foreach (var definition in _definitions)
        {
            var tag = definition.Group == GoldBoltOptionsGroup ? GoldBoltTag : CampaignTag;
            if (!_sections.TryGetValue(tag, out var fields)) continue;
            if (!fields.TryGetValue(definition.Name, out var field) || field.Values.Count != 1 || !Matches(definition.Kind, field.Values[0]))
                throw new SaveFormatException($"Missing or unsupported Rift Apart field '{definition.Name}'.");
        }
        if (_sections.TryGetValue(GoldBoltTag, out var gold) &&
            (!gold.TryGetValue("CollectedBoltHashes", out var hashes) ||
             (hashes.Kind != DdlValueKind.Null && hashes.Kind != DdlValueKind.UInt32)))
            throw new SaveFormatException("Unsupported collected gold bolt array.");
    }

    private static bool Matches(string expected, FieldValue value) => expected switch
    {
        "Integer" => value.Kind == FieldValueKind.Integer,
        "Number" => value.Kind is FieldValueKind.Integer or FieldValueKind.Float,
        "Bool" => value.Kind == FieldValueKind.Bool,
        _ => false,
    };

    private sealed record PlayerFieldDefinition(string Name, string Label, string Group, string Kind);
}
