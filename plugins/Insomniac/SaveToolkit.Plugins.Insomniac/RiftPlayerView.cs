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
    private static readonly IReadOnlyList<PlayerFieldDefinition> Definitions = LoadDefinitions();

    internal RiftPlayerView(Dat1Container container, DdlSections? sections = null)
    {
        foreach (var section in (sections ?? new DdlSections(container)).Roots.Where(s => s.Key is CampaignTag or GoldBoltTag or SpeedrunTag))
        {
            if (!_sections.TryAdd(section.Key, section.Value))
                throw new SaveFormatException("Duplicate Rift Apart player section.");
        }
        ValidateKnownFields();
    }

    internal bool HasCampaign => _sections.ContainsKey(CampaignTag);

    internal IReadOnlyList<ViewNode> Build(Func<string, string, FieldSpec, FieldValue, FieldNode> bind)
    {
        var result = new List<ViewNode>
        {
            new TextNode(RiftScalarWrites.Warning)
                { Tone = Tone.Warning },
        };
        if (!HasCampaign) return result;

        var campaign = _sections[CampaignTag];
        var bolts = campaign["CareerBolts"].Values[0].AsInteger() - campaign["SpentBolts"].Values[0].AsInteger();
        var playtime = campaign["Playtime"].Values[0].AsDouble();
        result.Add(new StatsNode([
            new Stat("Bolt counter difference", bolts.ToString(CultureInfo.InvariantCulture)) { Hint = "CareerBolts minus SpentBolts; differs from the stored inventory count." },
            new Stat("Play time (hours)", (playtime / SecondsPerHour).ToString("0.00", CultureInfo.InvariantCulture))
                { Hint = "Seconds are inferred from the timer scale and snapshot progression." },
        ]));
        result.Add(bind("player/AvailableBolts", "Career minus spent bolts (diagnostic)",
            new IntegerSpec(-(Int128)ulong.MaxValue, ulong.MaxValue), FieldValue.Of(bolts)) with
        {
            Confidence = Confidence.Inferred,
            Description = "CareerBolts minus SpentBolts; this diagnostic differs from the config-anchored inventory count. See Currencies for the read-only wallet candidate.",
        });

        foreach (var group in Definitions.GroupBy(d => d.Group))
        {
            var fields = group.Select(definition => BuildField(definition, bind)).OfType<ViewNode>().ToArray();
            if (fields.Length > 0) result.Add(new SectionNode(group.Key, fields));
        }

        result.Add(new SectionNode("Collectibles", Collectibles(bind)));
        result.Add(LevelTable());
        result.Add(new TextNode("Inventory shows inferred per-record XP/ammo slots; ownership, weapon names, health and level conversions remain unproven. Career weapon XP is an aggregate, and CurrentLevel is a location code.")
            { Tone = Tone.Info });
        return result;
    }

    private FieldNode? BuildField(PlayerFieldDefinition definition, Func<string, string, FieldSpec, FieldValue, FieldNode> bind)
    {
        var tag = definition.Group == GoldBoltOptionsGroup ? GoldBoltTag : CampaignTag;
        if (!_sections.TryGetValue(tag, out var fields)) return null;
        var field = fields[definition.Name];
        var description = $"Stored field {field.Name}; name hash matches; serialized type {field.Kind}. {DdlSections.Attribution}";
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
        if (definition.Name == "Playtime" && spec is FloatSpec timer)
            spec = timer with { Format = FloatFormat.Duration, UnitsPerSecond = 1 };
        var node = bind($"player/{field.Name}", definition.Label, spec, field.Values[0]);
        return node with
        {
            Confidence = node.Spec.ReadOnly ? Confidence.Verified : Confidence.Inferred,
            Description = node.Spec.ReadOnly ? description : description + " " + RiftScalarWrites.Warning +
                (node.Spec is IntegerSpec ? " Numeric edits retain the stored width and conservative observed range, not a proven game cap." : ""),
            Warning = node.Spec.ReadOnly ? warning : RiftScalarWrites.Warning,
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
            Description = "Names and array indices are verified; planet names are unknown. Arrays need not equal lifetime collectible totals. " + DdlSections.Attribution,
        };
    }

    private void ValidateKnownFields()
    {
        foreach (var definition in Definitions)
        {
            var tag = definition.Group == GoldBoltOptionsGroup ? GoldBoltTag : CampaignTag;
            if (!_sections.TryGetValue(tag, out var fields)) continue;
            if (!fields.TryGetValue(definition.Name, out var field) || field.Values.Count != 1 || !Matches(definition.Kind, field.Values[0]))
                throw new SaveFormatException($"Missing or unsupported Rift Apart field '{definition.Name}'.");
            if (definition.Kind == "Integer" && (field.Values[0].AsInteger() < 0 || field.Values[0].AsInteger() > ulong.MaxValue))
                throw new SaveFormatException($"Unsupported negative or oversized Rift Apart counter '{definition.Name}'.");
        }
        if (_sections.TryGetValue(CampaignTag, out var campaign))
        {
            foreach (var name in new[] { "LevelBolts", "LevelExp", "LevelWeaponExp", "LevelRaritanium", "LevelGoldBolts", "LevelRYNOPlans", "LevelVanityPickups" })
                if (campaign.TryGetValue(name, out var field) && field.Values.Any(value =>
                    value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > ulong.MaxValue))
                    throw new SaveFormatException($"Unsupported Rift Apart location counter array '{name}'.");
            foreach (var name in new[] { "VanityPickupsCollected", "RuinTokensCollected", "ZerpStonesCollected" })
                if (campaign.TryGetValue(name, out var field) &&
                    (field.Values.Count != 1 || field.Values[0].Kind != FieldValueKind.Integer ||
                     field.Values[0].AsInteger() < 0 || field.Values[0].AsInteger() > ulong.MaxValue))
                    throw new SaveFormatException($"Unsupported Rift Apart bit mask '{name}'.");
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

    private static IReadOnlyList<PlayerFieldDefinition> LoadDefinitions()
    {
        using var stream = typeof(RiftPlayerView).Assembly.GetManifestResourceStream(DefinitionsResource)!;
        return JsonSerializer.Deserialize<PlayerFieldDefinition[]>(stream)!;
    }
}
