using System.Buffers.Binary;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using static SaveToolkit.Plugins.TalesOfArise.AriseSchema;

namespace SaveToolkit.Plugins.TalesOfArise;

/// <summary>Character sheets and progress evidence keep source facts separate from undecoded state.</summary>
internal sealed class AriseReadOnlyViews
{
    private readonly AriseSave _save;
    private readonly AriseReferences _references;
    private readonly IReadOnlyDictionary<string, AriseWordCollection> _words;
    private static readonly (string Label, string[] Sections, string Capture)[] ProgressDomains =
    [
        ("Owls", ["SearchOwlSaveData"], "Capture discovery, reporting to the Owl King, and claiming the reward separately. An aggregate counter is not discovery state."),
        ("Quests", ["QuestSaveDataEx", "QuestEnemyCountSaveData", "SaveData_ScenaioFlg"], "Capture one objective, stage transition, completion, and reward claim separately; note quest name and displayed stage."),
        ("Fishing", ["FishingSaveData"], "Capture one new species, a repeat catch, and the Fishing Expert reward separately; note the species and notes screen."),
        ("Cooking and artifacts / NG+", ["PartyProfile", "SaveData_ItemManager"], "Capture recipe learned and first cooking separately; artifact acquisition, effect toggle, and NG+ carry-over separately. Section assignment for these states is unproven."),
        ("Camps and map unlocks", ["CampPointSaveData", "MapGimmickSaveData"], "Capture one camp and one map unlock separately with a control re-save and displayed location."),
    ];

    public AriseReadOnlyViews(AriseSave save, AriseReferences references)
    {
        _save = save;
        _references = references;
        var names = ProgressDomains.SelectMany(domain => domain.Sections)
            .Concat(Enumerable.Range(0, AriseSchema.CharacterNames.Count).Select(index => $"ArisePCStatus_{index:000}"))
            .Append("PartyOrder").ToHashSet(StringComparer.Ordinal);
        _words = save.Sections.Where(section => names.Contains(section.Name))
            .Select(section => new AriseWordCollection(save, section)).ToDictionary(words => words.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<QueryableCollection> Collections => _words.Values.Select(words => words.Metadata).ToArray();
    public bool Handles(string id) => _words.ContainsKey(id);
    public QueryableCollectionPage Query(string id, QueryableCollectionQuery query, Func<string, string, int, FieldNode> field) => _words[id].Query(query, field);

    public IReadOnlyList<FieldNode> PartyCpView(Func<string, string, Func<FieldValue>, FieldNode> field)
    {
        var party = _save.Sections.Single(section => section.Name == "PartyProfile");
        return [Cp("party/current-cp", "Current CP (inferred)", CurrentCpOffset), Cp("party/max-cp", "Max CP (inferred)", MaximumCpOffset)];

        FieldNode Cp(string id, string label, int relative) => field(id, label,
            () => FieldValue.Of((long)BinaryPrimitives.ReadUInt16LittleEndian(_save.Payload.AsSpan(party.Offset + relative)))) with
        {
            Confidence = Confidence.Inferred,
            Description = $"PartyProfile body +0x{relative:X2}, uint16 little-endian. " + CpAttribution,
            Warning = "Read-only: no controlled PS5 CP spend/refill or level-up pair establishes current/max authority or dependencies. No CP restoration or maximum editing is enabled.",
        };
    }

    public IReadOnlyList<ViewNode> CharacterView(Func<string, string, int, FieldNode> field) =>
    [
        new TextNode("These read-only sheets expose the existing inferred PS5 scalar mappings. Level-5 status blocks may be preinitialized; recruitment and party availability are Unknown. Arte use, proficiency, title unlock, skill purchase and panel completion have no proven save joins.") { Tone = Tone.Warning },
        .. Enumerable.Range(0, AriseSchema.CharacterNames.Count).Select(index => Character(index, field)),
        Evidence("PartyOrder"),
    ];

    public IReadOnlyList<ViewNode> BuildView() =>
    [
        new TextNode("Arte and skill prerequisites, published costs and effects are reference facts. Learned/unlocked/purchased state, usage, proficiency and title bonuses in this save are Unknown. Purchase, refund, unlock-all and presets remain unavailable.") { Tone = Tone.Warning },
        _references.CatalogSection(AriseReferences.ArtesCatalogId),
        _references.CatalogSection(AriseReferences.SkillsCatalogId),
        _references.CatalogSection(AriseReferences.TitlesCatalogId),
        _references.CatalogSection(AriseReferences.SkillNodesCatalogId),
    ];

    public IReadOnlyList<ViewNode> ProgressView() =>
    [
        new TextNode("Completion, reported/rewarded state, recipe knowledge, catches, artifact ownership and effect toggles are Unknown. No completion percentage is calculated from raw words. Counts and guide order are not save identities; progression writes remain unavailable.") { Tone = Tone.Warning },
        _references.CatalogSection(AriseReferences.ProgressCatalogId),
        .. ProgressDomains.Select(domain => new SectionNode(domain.Label,
        [
            new TextNode("Save state: Unknown. " + domain.Capture) { Tone = Tone.Info },
            .. domain.Sections.Select(Evidence),
        ])),
    ];

    private SectionNode Character(int index, Func<string, string, int, FieldNode> field)
    {
        var section = _save.Sections.Single(section => section.Name == $"ArisePCStatus_{index:000}");
        if (index >= MainCharacterCount)
        {
            return new SectionNode($"Unmapped status block {index}",
            [
                new TextNode("Character identity and scalar meanings are Unknown; this block is not treated as an available party member."),
                Evidence(section.Name),
            ]) { Collapsed = true };
        }

        var record = _references.Character(index);
        var label = record?.Attributes.GetValueOrDefault("character") ?? AriseSchema.CharacterNames[index];
        (string Id, string Label, int Offset)[] scalars =
        [
            ("level", "Level", LevelOffset), ("hp", "Current HP", CurrentHpOffset), ("experience", "Experience", ExperienceOffset),
            ("sp", "Unspent SP", SkillPointsOffset), ("base-hp", "Base HP", BaseHpOffset),
        ];
        return new SectionNode(label,
        [
            new TextNode("Recruitment: Unknown · Equipped gear: Unknown · Artes/proficiency: Unknown · Titles/skills/bonuses: Unknown") { Tone = Tone.Muted },
            .. scalars.Select(scalar => field($"sheet/{index}/{scalar.Id}", scalar.Label, section.Offset + scalar.Offset) with
            {
                Confidence = Confidence.Inferred,
                Description = $"Body +0x{scalar.Offset:X}; PS4/PC mapping is inferred on PS5. " + (record?.AttributionText ?? AriseSchema.CharacterAttribution),
            }),
            Evidence(section.Name),
        ]) { Description = record?.AttributionText ?? AriseSchema.CharacterAttribution, Collapsed = true };
    }

    private SectionNode Evidence(string name)
    {
        var section = _save.Sections.Single(section => section.Name == name);
        return new SectionNode($"{name} · read-only evidence",
        [
            new TextNode($"Schema 0x{section.Id:X8} · version {section.Version} · {section.Size} bytes. Raw words have Unknown gameplay meaning.") { Monospace = true },
            _words[$"evidence/{section.Id:X8}"].View,
        ]) { Collapsed = true, Description = SectionAttribution };
    }
}
