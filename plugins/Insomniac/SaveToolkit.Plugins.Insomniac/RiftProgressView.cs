using System.Globalization;
using System.Text;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed class RiftProgressView(DdlSections sections, RiftCollections collections,
    Func<string, string, FieldSpec, FieldValue, FieldNode> bind)
{
    internal IReadOnlyList<ViewNode> Build() =>
    [
        collections.Add("world/objectives", "Mission and objective states",
            [new("graph", "Graph key"), new("node", "Node key"), new("code", "Stored mission code"),
             new("state", "Stored state"), new("count", "Completed count", IntegerSpec.UInt32)],
            Objectives(), "Mission codes are joined at runtime by their validated name CRC to NewMissionsViewed; this records viewed menu entries, not ownership/completion. States/counts come from each graph node. " + DdlSections.Attribution),
        collections.Add("world/dimensions", "Pocket dimension states",
            [new("key", "Dimension key"), new("state", "Complete")],
            Map(DdlSections.DimensionTag, "DimensionSaves").Select(entry => SimpleRow("dimension", entry, "Complete")),
            "Stored Complete flags; dimension IDs are not joined to planet, portal number or guide coordinates. " + DdlSections.Attribution),
        collections.Add("world/globals", "Global progress values",
            [new("key", "Stored key"), new("value", "Stored value")],
            Map(0x9E13_3672, "Table").Select(entry => new RiftRow(entry.Name, new Dictionary<string, RiftCell>
            {
                ["key"] = RiftCell.TextOnly(entry.Name),
                ["value"] = Scalar($"world/globals/{entry.Name}", entry),
            })), "Raw keyed progress values. No trophy, arena reward or completion meaning is assigned to unknown keys. " + DdlSections.Attribution),
    ];

    private IEnumerable<RiftRow> Objectives()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (sections.Roots.TryGetValue(RiftPlayerView.CampaignTag, out var campaign) &&
            campaign.TryGetValue("NewMissionsViewed", out var viewed))
            foreach (var value in viewed.Values)
            {
                var code = value.AsString();
                names.TryAdd(DdlReader.NameHash(Encoding.UTF8.GetBytes(code)).ToString(CultureInfo.InvariantCulture), code);
            }
        foreach (var graph in Map(DdlSections.ObjectivesTag, "GraphDataTable"))
        foreach (var node in Children(Child(graph, "NodeDataTable")))
        {
            var state = Child(Child(node, "NodeData"), "Obj");
            var fields = state?.Objects.SingleOrDefault()?.Fields;
            var status = fields?.GetValueOrDefault("MissionState") ?? fields?.GetValueOrDefault("ObjectiveState") ?? fields?.GetValueOrDefault("WasStarted");
            var count = fields?.GetValueOrDefault("CompletedCount");
            var id = $"world/objective/{graph.Name}/{node.Name}";
            yield return new(id, new Dictionary<string, RiftCell>
            {
                ["graph"] = RiftCell.TextOnly(graph.Name), ["node"] = RiftCell.TextOnly(node.Name),
                ["code"] = RiftCell.TextOnly(names.GetValueOrDefault(node.Name, "Unresolved")),
                ["state"] = Scalar(id + "/state", status), ["count"] = Scalar(id + "/count", count),
            });
        }
    }

    private RiftRow SimpleRow(string kind, DdlField entry, string field) => new(entry.Name, new Dictionary<string, RiftCell>
    {
        ["key"] = RiftCell.TextOnly(entry.Name), ["state"] = Scalar($"world/{kind}/{entry.Name}", Child(entry, field)),
    });

    private RiftCell Scalar(string id, DdlField? field)
    {
        if (field is not { Values.Count: 1 }) return RiftCell.TextOnly("Not stored / unresolved layout");
        var value = field.Values[0];
        decimal? number = value.Kind == FieldValueKind.Integer ? (decimal)value.AsInteger() : null;
        return RiftCollections.Field(value.ToString(), () => bind(id, field.Name, field.Spec, value) with
        {
            Confidence = Confidence.Verified,
            Description = $"Stored {field.Name} ({field.Kind}). No additional gameplay meaning is assumed. " + DdlSections.Attribution,
        }, number);
    }

    private IEnumerable<DdlField> Map(uint tag, string name) => sections.Roots.TryGetValue(tag, out var root)
        ? Children(root.GetValueOrDefault(name)) : [];
    private static IEnumerable<DdlField> Children(DdlField? field) => field?.Objects.SelectMany(o => o.Fields.Values) ?? [];
    private static DdlField? Child(DdlField? field, string name) => field?.Objects.Count == 1
        ? field.Objects[0].Fields.GetValueOrDefault(name) : null;
}
