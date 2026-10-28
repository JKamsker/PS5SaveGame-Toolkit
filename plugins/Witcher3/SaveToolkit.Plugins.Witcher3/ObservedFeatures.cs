using System.Security.Cryptography;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>Optional readers fail independently, preserving the raw explorer and original-image writer.</summary>
internal sealed class ObservedFeatures
{
    internal const int PageSize = 64;
    private readonly byte[] _data;
    private readonly IReadOnlyList<string> _names;
    private readonly IReferenceCatalog? _abilities;
    private readonly IReferenceCatalog? _gwent;
    private readonly WriteFields? _writeFields;
    public IReadOnlyList<AbilityArray> Abilities { get; } = [];
    private readonly IReadOnlyList<GwentCard>? _originalCards;
    public IReadOnlyList<GwentCard>? Cards => _originalCards?.Select((c, index) => c with {
        Copies = CopyEdits[index].Value,
    }).ToArray();
    public SkillRanks? SkillEdits { get; }
    public IReadOnlyList<GwentCopies> CopyEdits { get; } = [];
    public IReadOnlyList<string> Warnings { get; }
    private AbilityCollections AbilityTables => new(Abilities, _abilities, SkillEdits, AbilityRow);
    public IReadOnlyList<QueryableCollection> Collections => AbilityTables.Collections;
    public QueryableCollectionPage Query(string id, QueryableCollectionQuery query) => AbilityTables.Query(id, query);

    public ObservedFeatures(PlayerData? player, Sav3Tree tree, byte[] data, IReadOnlyList<string> names,
        IReferenceCatalog? abilities = null, IReferenceCatalog? gwent = null, WriteFields? writeFields = null)
    {
        _data = data;
        _names = names;
        _abilities = abilities;
        _gwent = gwent;
        _writeFields = writeFields;
        var warnings = new List<string>();
        if (player is not null)
        {
            if (player.AbilityManager is null) { warnings.Add("No unique supported ability manager was found; use the raw explorer for saved ability data."); }
            try { if (player.AbilityManager is { } manager) { Abilities = AbilityReader.Read(manager, data, names); } }
            catch (SaveFormatException ex) { warnings.Add($"Ability reader unavailable: {ex.Message}"); }
            try
            {
                if (player.AbilityManager is { } manager && Abilities.SingleOrDefault(a => a.Name == "skills") is { } skills)
                {
                    SkillEdits = new SkillRanks(manager, data, names, skills);
                }
            }
            catch (SaveFormatException ex) { warnings.Add($"Skill editing unavailable: {ex.Message}"); }
            try
            {
                _originalCards = GwentReader.Read(tree, data);
                CopyEdits = _originalCards?.Select(c => new GwentCopies(c)).ToArray() ?? [];
            }
            catch (SaveFormatException ex) { warnings.Add($"Gwent reader unavailable: {ex.Message}"); }
        }

        Warnings = warnings;
    }

    public IReadOnlyList<ViewNode> Build() => [
        new TextNode("Experimental non-core skill ranks and existing Gwent counts are editable. Core skills with level 1 are not purchased skills. " +
            "Default/sentinel records are retained. Localization keys and icon paths are identifiers, not bundled game text or artwork."),
        new TextNode(WriteFields.SkillWarning + " " + WriteFields.GwentWarning) { Tone = Tone.Warning },
        .. Warnings.Select(w => new TextNode(w) { Tone = Tone.Warning }),
        SectionNode.Lazy("Skill definition graph", "ability-graph/skills"),
        SectionNode.Lazy("Mutation prerequisite graph", "ability-graph/mutations"),
        .. AbilityTables.Build(),
        .. Abilities.SelectMany(a => Enumerable.Range(0, Math.Max(1, (a.Entries.Count + PageSize - 1) / PageSize))
            .Select(page => SectionNode.Lazy($"{a.Name}: linked details {page + 1}", $"ability/{a.Name}/{page * PageSize}", a.Entries.Count.ToString()))),
        .. (Cards is null ? new ViewNode[] { new TextNode("No supported Gwent collection was decoded.") } : [
            new StatsNode([new Stat("Gwent indices", Cards.Count.ToString()), new Stat("Saved copies", Cards.Sum(c => (long)c.Copies).ToString())]),
            SectionNode.Lazy("Gwent collection (native indices)", "gwent/0", Cards.Count.ToString()),
            new TextNode("Collection pairs stop at SBSelectedDeckIndex. Catalog targets are community expectations, not legal limits or achievement guarantees. " +
                "Not recorded does not prove a card is obtainable in this build. Deck contents remain separate."),
        ]),
    ];

    public IReadOnlyList<ViewNode> Expand(string lazyId)
    {
        if (lazyId is "ability-graph/skills" or "ability-graph/mutations")
        {
            return new AbilityGraphs(Abilities, _names, _abilities, SkillEdits).Build(lazyId["ability-graph/".Length..]);
        }
        var parts = lazyId.Split('/');
        if (parts.Length == 3 && parts[0] == "ability" && int.TryParse(parts[2], out var start) &&
            Abilities.SingleOrDefault(a => a.Name == parts[1]) is { } array && start >= 0 && start <= array.Entries.Count)
        {
            return [new TableNode([new("id", "Saved identity"), new("name", "Reference name"), new("rank", "Stored rank"), new("properties", "Input properties (read-only)")],
                array.Entries.Skip(start).Take(PageSize).Select(entry => AbilityRow(array, entry)).ToArray()) { Title = array.Name }];
        }

        if (parts.Length == 2 && parts[0] == "gwent" && int.TryParse(parts[1], out start) && Cards is { } cards && start >= 0 && start <= cards.Count)
        {
            return [new TableNode([new("index", "Native card index"), new("name", "Reference name"), new("copies", "Saved copies"), new("kind", "Collection group")],
                cards.Skip(start).Take(PageSize).Select(card => new TableRow(card.Index.ToString(), new Dictionary<string, ViewNode> {
                    ["index"] = new TextNode(card.Index.ToString()), ["copies"] = _writeFields is null ? new TextNode(card.Copies.ToString()) :
                        _writeFields.Copies(CopyEdits.Single(c => c.Card.Index == card.Index)),
                    ["name"] = ReferenceCell(_gwent?.Find(card.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)), card.Index.ToString()),
                    ["kind"] = new TextNode(card.Leader ? "Leader collection" : "Ordinary collection"),
                })).ToArray()), .. Next("gwent", start, cards.Count)];
        }

        throw new KeyNotFoundException($"Unknown feature page '{lazyId}'.");
    }

    public IEnumerable<Sav3Patch> Patches() => (SkillEdits?.Patches() ?? [])
        .Concat(CopyEdits.Select(c => c.Patch()).OfType<Sav3Patch>());

    public void Verify(ObservedFeatures reopened)
    {
        SkillEdits?.Verify(reopened.Abilities);
        if (Cards is { } cards && (reopened.Cards is null || !cards.Select(c => (c.Index, c.Copies, c.Leader))
            .SequenceEqual(reopened.Cards.Select(c => (c.Index, c.Copies, c.Leader)))))
        {
            throw new SaveFormatException("Edited Gwent counts or collection identities did not survive serialization.");
        }
    }

    private static ViewNode ReferenceCell(ReferenceRecord? record, string raw) => record is null
        ? new TextNode($"{raw} (unmapped; source: save-native identity)")
        : new SectionNode(record.Name, [new TextNode(record.Category ?? "Reference"), new TextNode(record.Description ?? ""),
            new TextNode(record.AttributionText)]) { Collapsed = true, Badge = "Inferred mapping" };

    private TableRow AbilityRow(AbilityArray array, AbilityEntry entry) => new(entry.Id, new Dictionary<string, ViewNode> {
        ["id"] = _writeFields is null ? new TextNode(entry.Id) : _writeFields.Identity(array.Name, entry, _abilities?.Find(entry.Id)),
        ["name"] = ReferenceCell(_abilities?.Find(entry.Id), entry.Id),
        ["rank"] = array.Name == "skills" && SkillEdits?.Find(entry.Id) is { } rank && _writeFields is not null
            ? _writeFields.Rank(rank) : new TextNode("Definition / read-only"),
        ["properties"] = new SectionNode("Encoded properties", entry.Record.Properties.Select(p => (ViewNode)new TextNode($"{p.Name}: {Value(p)}")).ToArray()) { Collapsed = true },
    });

    private string Value(TaggedProperty property)
    {
        if ((property.Type.StartsWith('E') || property.Type == "CName") && property.Value.Length == sizeof(ushort))
        {
            return AbilityReader.NameValue(property, _names);
        }

        var node = new Sav3Node(0, property.Offset, property.Bytes.Length, "property", property.Name, property.Type, property.Offset + 8, property.Value.Length);
        var scalar = ScalarReader.Read(node, _data, _names);
        return scalar is not null ? scalar.Value.ToString() :
            $"{property.Type}, {property.Value.Length} bytes, SHA-256 {Convert.ToHexString(SHA256.HashData(property.Value))}";
    }

    private static IEnumerable<ViewNode> Next(string prefix, int start, int count) =>
        start + PageSize < count ? [SectionNode.Lazy("Next records", $"{prefix}/{start + PageSize}")] : [];
}
