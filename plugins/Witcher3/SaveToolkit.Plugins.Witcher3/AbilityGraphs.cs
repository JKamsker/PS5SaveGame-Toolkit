using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>Saved definitions only: rank zero does not establish availability and core ranks are not purchases.</summary>
internal sealed class AbilityGraphs(IReadOnlyList<AbilityArray> arrays, IReadOnlyList<string> names,
    IReferenceCatalog? catalog, SkillRanks? edits)
{
    public IReadOnlyList<ViewNode> Build(string kind)
    {
        var array = arrays.SingleOrDefault(a => a.Name == kind);
        if (array is null) { return [new TextNode($"No supported {kind} definitions were decoded.")]; }
        try
        {
            if (kind == "mutations") { return [Mutations(array)]; }
            var graphs = array.Entries.Chunk(SpatialViewLimits.MaxItems).Select((entries, page) => new GraphNode(
                $"Saved skill definitions · {page + 1}", entries.Select((e, index) => Skill(array, e, page * SpatialViewLimits.MaxItems + index)).ToArray(), [],
                "Saved ranks and branches; no prerequisite edges are inferred from positions or tiers. " +
                "Core definitions are not purchases. Rank zero and all availability states remain unknown. " +
                "Open a stored rank or identity for its original encoded properties and warnings.")).ToArray();
            foreach (var graph in graphs) { SpatialViewValidation.Validate(graph); }
            return graphs;
        }
        catch (Exception ex) when (ex is SaveFormatException or InvalidOperationException)
        {
            return [new TextNode($"{kind} graph unavailable: {ex.Message} Use the encoded-property tables.") { Tone = Tone.Warning }];
        }
    }

    private GraphItem Skill(AbilityArray array, AbilityEntry entry, int index)
    {
        var rank = edits?.Find(entry.Id);
        var level = rank?.Value ?? entry.Record.Integer("level");
        var maximum = entry.Record.Integer("maxLevel");
        var core = Flag(entry.Record, "isCoreSkill");
        var reference = catalog?.Find(entry.Id);
        return new GraphItem(entry.Id, reference?.Name ?? entry.Id,
            !core && rank is not null && level > 0 ? GraphState.Unlocked : GraphState.Unknown) {
            Branch = EnumName(entry.Record, "skillPath") ?? "Unspecified saved branch",
            Cost = $"Stored cost {entry.Record.Integer("cost").ToString(CultureInfo.InvariantCulture)} (not charged)",
            Description = $"{entry.Id}; stored rank {level}/{maximum}. " +
                (core ? "Core definition, not a purchased skill. " : "Stored rank does not prove equipped or usable. ") +
                WriteFields.SkillWarning + " " + Provenance(reference),
            Target = Target(array, index, entry, rank?.ValueId ?? WriteFields.IdentityId(array.Name, entry.Id)),
        };
    }

    private GraphNode Mutations(AbilityArray array)
    {
        if (array.Entries.Count > SpatialViewLimits.MaxItems) { throw new SaveFormatException("Too many mutation definitions for one prerequisite graph."); }
        var edges = new List<GraphEdge>();
        var items = array.Entries.Select((entry, index) => {
            foreach (var prerequisite in Requirements(entry.Record)) { edges.Add(new(prerequisite, entry.Id)); }
            var reference = catalog?.Find(entry.Id);
            return new GraphItem(entry.Id, reference?.Name ?? entry.Id, GraphState.Unknown) {
                Branch = "Saved mutation definitions",
                Description = $"{entry.Id}. Arrows follow saved requiredMutations, from prerequisite to dependent. " +
                    "Research, quest eligibility, consumed points/mutagens and activation are unknown; no unlock is inferred. " + Provenance(reference),
                Target = Target(array, index, entry, WriteFields.IdentityId(array.Name, entry.Id)),
            };
        }).ToArray();
        var graph = new GraphNode("Saved mutation prerequisites", items, edges,
            "Read-only prerequisites from requiredMutations. Every research/availability state is unknown. " +
            "Arrows do not authorize unlocking or imply that this character has reached the DLC quest.");
        SpatialViewValidation.Validate(graph);
        return graph;
    }

    private IReadOnlyList<string> Requirements(TaggedRecord record)
    {
        if (record.Find("requiredMutations") is not { } property) { return []; }
        if (property.Type != "array:2,0,EPlayerMutationType") { throw new SaveFormatException("Unsupported mutation prerequisite type."); }
        return RecipeReader.ReadNames(property.Value, names, "Mutation prerequisite");
    }

    private string? EnumName(TaggedRecord record, string key)
    {
        if (record.Find(key) is not { } property) { return null; }
        if (property.Type != "ESkillPath") { throw new SaveFormatException("Unsupported saved skill branch type."); }
        return AbilityReader.NameValue(property, names);
    }

    private static bool Flag(TaggedRecord record, string name)
    {
        if (record.Find(name) is not { } property) { return false; }
        if (property.Type != "Bool" || property.Value.Length != 1 || property.Value[0] > 1) { throw new SaveFormatException($"Invalid {name} flag."); }
        return property.Value[0] != 0;
    }

    private static string Provenance(ReferenceRecord? record) => record is null
        ? "Source: save-native definition; friendly identity unresolved."
        : $"Inferred reference mapping. {record.Description} {record.AttributionText}";

    private static ViewTarget Target(AbilityArray array, int index, AbilityEntry entry, string valueId) =>
        new(ViewTargetKind.Field, valueId) { Path = ["tab:Abilities and Gwent",
            $"section:ability/{array.Name}/{index / ObservedFeatures.PageSize * ObservedFeatures.PageSize}", $"section:{entry.Id}"] };
}
