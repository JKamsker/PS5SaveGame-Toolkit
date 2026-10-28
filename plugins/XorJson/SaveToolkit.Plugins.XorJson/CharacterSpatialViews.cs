using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson;

internal static class CharacterSpatialViews
{
    private const string ControlsTitle = "Quest graph controls";
    private const int CatalogPageSize = 200;

    public static ViewNode Quests(JsonValueToken root, IReferenceContext references, Func<JsonValueToken, ViewNode> view)
    {
        var catalog = references.Catalog("xorjson-world");
        if (catalog is null) return new TextNode("Quest dependency graph needs the attributed community reference pack.");
        var records = new List<ReferenceRecord>();
        for (var offset = 0; ; offset += CatalogPageSize)
        {
            var page = catalog.Search(new ReferenceQuery { Category = "Quest", Offset = offset, Limit = CatalogPageSize });
            records.AddRange(page.Items);
            if (records.Count >= page.Total || page.Items.Count == 0) break;
        }
        var rows = root.Member("CharacterSaveV1")?.Member("quest")?.Member("Quests")?.Children ?? [];
        var byName = rows.Where(row => row.Member("QuestName")?.Scalar.Kind == FieldValueKind.Text)
            .ToLookup(row => row.Member("QuestName")!.Scalar.AsString(), StringComparer.Ordinal);
        var items = new List<GraphItem>();
        var controls = new List<ViewNode>();
        foreach (var record in records.Take(SpatialViewLimits.MaxItems))
        {
            var matching = byName[record.Key].ToArray();
            var state = matching.Length == 1 ? matching[0].Member("State") : null;
            var target = new ViewTarget(ViewTargetKind.Action, "json/inspect") { Path = ["tab:Overview"] };
            if (state is { IsContainer: false })
            {
                controls.Add(view(state));
                target = new(ViewTargetKind.Field, state.ValueId) { Path = ["tab:Quests", "section:Quest dependencies (Inferred)", "section:" + ControlsTitle] };
            }
            items.Add(new(record.Key, record.Name, State(state))
            {
                Branch = state is null ? "Absent or ambiguous (Unknown)" : "Stored quest",
                Description = $"{record.Key}. Stored state: {state?.Scalar.ToString() ?? "Unknown"}. Inferred PC unlock relationships, not verified PS5 prerequisites. {record.AttributionText}",
                Target = target,
            });
        }
        var keys = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var edges = records.Where(record => keys.Contains(record.Key))
            .SelectMany(record => record.Attributes.GetValueOrDefault("prerequisites", "").Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Where(keys.Contains).Select(parent => new GraphEdge(parent, record.Key))).Distinct().ToArray();
        var graph = new GraphNode("Community quest unlock relationships (Inferred)", items, edges,
            "Arrows are PC community follows relationships: completion may unlock the next quest. This is not a complete PS5 dependency model. " +
            "Completed maps to Unlocked, Available/Active to Available, Unavailable to Locked; absent, duplicated and unrecognized states stay Unknown. " +
            "Raw quests and tasks remain in the paged tables. No reward or task dependency is inferred from array order.");
        return new SectionNode("Quest dependencies (Inferred)", [graph, new SectionNode(ControlsTitle, controls) { Collapsed = true }]);
    }

    private static GraphState State(JsonValueToken? token) => token?.Scalar is { Kind: FieldValueKind.Text } value ? value.AsString() switch
    {
        "Completed" => GraphState.Unlocked,
        "Available" or "Active" => GraphState.Available,
        "Unavailable" => GraphState.Locked,
        _ => GraphState.Unknown,
    } : GraphState.Unknown;
}
