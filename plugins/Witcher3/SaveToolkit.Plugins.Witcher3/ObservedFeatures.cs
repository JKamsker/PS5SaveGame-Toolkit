using System.Security.Cryptography;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>Optional readers fail independently, preserving the raw explorer and original-image writer.</summary>
internal sealed class ObservedFeatures
{
    private const int PageSize = 128;
    private readonly byte[] _data;
    private readonly IReadOnlyList<string> _names;
    public IReadOnlyList<AbilityArray> Abilities { get; } = [];
    public IReadOnlyList<GwentCard>? Cards { get; }
    public IReadOnlyList<string> Warnings { get; }

    public ObservedFeatures(PlayerData? player, Sav3Tree tree, byte[] data, IReadOnlyList<string> names)
    {
        _data = data;
        _names = names;
        var warnings = new List<string>();
        if (player is not null)
        {
            if (player.AbilityManager is null) { warnings.Add("No unique supported ability manager was found; use the raw explorer for saved ability data."); }
            try { if (player.AbilityManager is { } manager) { Abilities = AbilityReader.Read(manager, data, names); } }
            catch (SaveFormatException ex) { warnings.Add($"Ability reader unavailable: {ex.Message}"); }
            try { Cards = GwentReader.Read(tree, data); }
            catch (SaveFormatException ex) { warnings.Add($"Gwent reader unavailable: {ex.Message}"); }
        }

        Warnings = warnings;
    }

    public IReadOnlyList<ViewNode> Build() => [
        new TextNode("Read-only saved definitions and links. Core skills with level 1 are not purchased skills. " +
            "Default/sentinel records are retained. Localization keys and icon paths are identifiers, not bundled game text or artwork."),
        .. Warnings.Select(w => new TextNode(w) { Tone = Tone.Warning }),
        .. Abilities.Select(a => SectionNode.Lazy(a.Name, $"ability/{a.Name}/0", a.Entries.Count.ToString())),
        .. (Cards is null ? new ViewNode[] { new TextNode("No supported Gwent collection was decoded.") } : [
            new StatsNode([new Stat("Gwent indices", Cards.Count.ToString()), new Stat("Saved copies", Cards.Sum(c => (long)c.Copies).ToString())]),
            SectionNode.Lazy("Gwent collection (native indices)", "gwent/0", Cards.Count.ToString()),
            new TextNode("Collection pairs stop at SBSelectedDeckIndex. Deck contents, achievements and missing-card expectations are not inferred."),
        ]),
    ];

    public IReadOnlyList<ViewNode> Expand(string lazyId)
    {
        var parts = lazyId.Split('/');
        if (parts.Length == 3 && parts[0] == "ability" && int.TryParse(parts[2], out var start) &&
            Abilities.SingleOrDefault(a => a.Name == parts[1]) is { } array && start >= 0 && start <= array.Entries.Count)
        {
            return [new TableNode([new("id", "Saved identity"), new("properties", "Saved properties (read-only)")],
                array.Entries.Skip(start).Take(PageSize).Select(entry => new TableRow(entry.Id, new Dictionary<string, ViewNode> {
                    ["id"] = new TextNode(entry.Id),
                    ["properties"] = new SectionNode("Encoded properties", entry.Record.Properties.Select(p => (ViewNode)new TextNode($"{p.Name}: {Value(p)}")).ToArray()) { Collapsed = true },
                })).ToArray()) { Title = array.Name }, .. Next($"ability/{array.Name}", start, array.Entries.Count)];
        }

        if (parts.Length == 2 && parts[0] == "gwent" && int.TryParse(parts[1], out start) && Cards is { } cards && start >= 0 && start <= cards.Count)
        {
            return [new TableNode([new("index", "Native card index"), new("copies", "Saved copies"), new("kind", "Collection group")],
                cards.Skip(start).Take(PageSize).Select(card => new TableRow(card.Index.ToString(), new Dictionary<string, ViewNode> {
                    ["index"] = new TextNode(card.Index.ToString()), ["copies"] = new TextNode(card.Copies.ToString()),
                    ["kind"] = new TextNode(card.Leader ? "Leader collection" : "Ordinary collection"),
                })).ToArray()), .. Next("gwent", start, cards.Count)];
        }

        throw new KeyNotFoundException($"Unknown feature page '{lazyId}'.");
    }

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
