using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

/// <summary>PC opening-condition evidence, never a claimed PS5 completion or unlock state.</summary>
internal sealed class MissionReferences(PragmataDocument document)
{
    internal const string InspectAction = "research/inspect-mission";
    private const string CatalogueTitle = "Mission reference catalogue";

    public IReadOnlyList<ViewNode> Build()
    {
        if (document.References.Missions is not { } missions || missions.Search(new ReferenceQuery { Limit = 1 }).Items.FirstOrDefault() is not { } first)
            return [new TextNode("Mission prerequisite references require the attributed Pragmata evidence pack. Saved quest states remain in the existing values below.")];

        var items = new List<GraphItem>();
        var edges = new List<GraphEdge>();
        var fields = new Dictionary<string, FieldNode>(StringComparer.Ordinal);
        foreach (var record in missions.Search(new ReferenceQuery { Limit = 200 }).Items)
        {
            if (!uint.TryParse(record.Attributes.GetValueOrDefault("openingItemId"), NumberStyles.None, CultureInfo.InvariantCulture, out var raw) ||
                document.References.ItemEvidence(raw) is not { } evidence) continue;
            var itemId = $"item/{raw}";
            if (!fields.ContainsKey(itemId))
            {
                var field = document.References.Browse(ReferenceReadouts.ItemEvidenceCatalog, evidence);
                fields[itemId] = field;
                items.Add(new GraphItem(itemId, $"Opening-condition item 0x{raw:x8}", GraphState.Unknown)
                {
                    Branch = "PC reference only", Description = field.Description, Target = Target(field),
                });
            }
            var missionId = $"mission/{record.Key}";
            var mission = document.References.Browse(ReferenceReadouts.MissionsCatalog, record);
            fields[missionId] = mission;
            items.Add(new GraphItem(missionId, record.Name, GraphState.Unknown)
            {
                Branch = record.Category,
                Description = $"{mission.Description} Other chapter/runtime conditions may also apply. No PS5 mission state is inferred from quantity.",
                Target = Target(mission),
            });
            edges.Add(new GraphEdge(itemId, missionId));
        }
        return [new GraphNode("Mission opening-condition references", items, edges,
            "Arrows show PC OpenConditionItemID references to mission IDs. All saved unlock/completion states are Unknown. " +
            "These are incomplete prerequisite rules, not a skill tree or a promise that replacing an item unlocks a mission."),
            new SectionNode(CatalogueTitle, fields.Values.Cast<ViewNode>().ToArray()),
            new ActionNode(InspectAction, "Inspect mission reference")
            {
                Mutates = false,
                Description = "Inspect attributed PC reference metadata. Does not change save values, unlocks, revision or undo history.",
                Parameters = [new FieldNode("mission", "Mission reference", new ReferenceSpec(ReferenceReadouts.MissionsCatalog), FieldValue.Of(first.Key))],
            }];
    }

    public ActionOutcome Inspect(IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (parameters.Count != 1 || !parameters.TryGetValue("mission", out var value) || value.Kind != FieldValueKind.Text ||
            document.References.Missions?.Find(value.AsString()) is not { } record)
            throw new InvalidOperationException("Select one mission from the pinned reference catalogue.");
        return new ActionOutcome
        {
            Message = $"{record.Name} / {record.Key}. {record.Description} " +
                string.Join("; ", record.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}")) +
                $". {record.AttributionText}",
            Warnings = ["PC reference only. Localized names, PS5 prerequisite behavior and saved completion are Unknown. No save bytes were changed."],
        };
    }

    private static ViewTarget Target(FieldNode field) => new(ViewTargetKind.Field, field.ValueId)
        { Path = ["tab:Quests", $"section:{CatalogueTitle}"] };
}
