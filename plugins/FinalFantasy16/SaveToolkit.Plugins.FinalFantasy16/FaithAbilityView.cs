using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal sealed class FaithAbilityView(FaithCatalog catalog)
{
    public const string LazyId = "eikon-ability-reference";
    public const string SectionTitle = "Eikon ability tree (spoilers)";
    public const string TabTitle = "Ability reference";
    private static readonly IReadOnlyDictionary<string, string> StageLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["learn"] = "Initial rank / learn", ["upgrade"] = "Upgrade", ["master"] = "Master",
    };

    public IReadOnlyList<ViewNode> Build(FaithContextField field)
    {
        var abilities = catalog.Abilities().OrderBy(ability => ability.Key, StringComparer.Ordinal).ToArray();
        var items = new List<GraphItem>();
        var edges = new List<GraphEdge>();
        foreach (var ability in abilities)
        {
            string? previous = null;
            foreach (var stage in Stages(ability))
            {
                var id = $"ability/{ability.Key}/{stage}";
                items.Add(new GraphItem(id, $"{FaithCatalog.PlainText(ability.Name)} — {StageLabels[stage]}", GraphState.Unknown)
                {
                    Branch = ability.Category ?? "Unknown Eikon",
                    Cost = ability.Attributes.TryGetValue(stage + "Ap", out var cost) ? $"{cost} AP (this stage)" : "AP cost Unknown",
                    Description = FaithCatalog.Describe(ability) + " Eikon relationship: " +
                        ability.Attributes.GetValueOrDefault("relationSource") +
                        " Edges describe reference progression of this ability only; they do not connect different abilities or prove save prerequisites.",
                    Target = new ViewTarget(ViewTargetKind.Field, FieldId(ability))
                    {
                        Path = [$"tab:{TabTitle}", $"section:{LazyId}", $"row:{FieldId(ability)}"],
                    },
                });
                if (previous is not null) edges.Add(new(previous, id));
                previous = id;
            }
        }
        var graph = new GraphNode("Eikon ability progression — reference only", items, edges,
            $"{abilities.Length} reference abilities, {abilities.Select(ability => ability.Category).Distinct().Count()} Eikon groups. " +
            "Every saved learned/mastered/unlocked state is Unknown. Each chain describes one ability's published progression, " +
            "not a relationship between its reference key and a stored rank byte. AP costs are individual reference annotations; " +
            "missing costs are Unknown. No AP/rank/reset preset or automatic refund is applied.");
        SpatialViewValidation.Validate(graph);
        return [new TextNode("Spoilers: base-game and Rising Tide Eikons/abilities. Reference names do not establish DLC ownership, " +
                    "build availability or current progression. The 68 stored rank-byte identities remain Unknown; raw controls are on Character context.")
                { Tone = Tone.Warning },
            graph,
            new TableNode([new("ability", "Reference ability"), new("eikon", "Eikon / evidence")],
                abilities.Select(ability => new TableRow(FieldId(ability), new Dictionary<string, ViewNode>
                {
                    ["ability"] = field(FieldId(ability), FaithCatalog.PlainText(ability.Name), new ReferenceSpec(FaithCatalog.AbilitiesId),
                        () => FieldValue.Of(ability.Key), Confidence.Inferred, FaithCatalog.Describe(ability)),
                    ["eikon"] = new SectionNode(ability.Category ?? "Unknown Eikon", [
                        new TextNode(FaithCatalog.Describe(ability)),
                        new TextNode("Relationship source: " + ability.Attributes.GetValueOrDefault("relationSource")),
                        new TextNode("Cost evidence: " + ability.Attributes.GetValueOrDefault("costEvidence")),
                    ]) { Collapsed = true },
                })).ToArray()) { Title = "Reference ability details — no saved rank association" }];
    }

    private static string FieldId(ReferenceRecord ability) => $"abilities/{ability.Key}";

    // Host packs can add annotations, but cannot introduce arbitrary graph stages or edges.
    private static IEnumerable<string> Stages(ReferenceRecord ability) =>
        (ability.Attributes.GetValueOrDefault("stages") ?? "learn").Split(',')
            .Where(StageLabels.ContainsKey).Distinct(StringComparer.Ordinal);
}
