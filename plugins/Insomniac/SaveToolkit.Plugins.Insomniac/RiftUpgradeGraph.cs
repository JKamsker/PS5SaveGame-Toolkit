using System.Globalization;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

/// <summary>Stored grid rows, without inventing node names, costs or prerequisite edges.</summary>
internal static class RiftUpgradeGraph
{
    internal const string Boundary = "Sixteen stored ModGrid row masks, not sixteen proven upgrades. " +
        "Bit order, purchased-node meaning, costs and prerequisites are unknown; no edges or unlock states are inferred. ";

    internal static SectionNode Lazy(InventoryBlock block, InventoryRecord record,
        Dictionary<string, Func<IReadOnlyList<ViewNode>>> lazy,
        Func<string, string, FieldSpec, FieldValue, FieldNode> bind)
    {
        var id = $"upgrade-graph/{block.Key}/{record.Key}";
        lazy[id] = () => Build(block, record, id, bind);
        return SectionNode.Lazy("Stored grid diagram", id, "Read-only") with
        {
            Description = Boundary + DdlSections.Attribution,
        };
    }

    private static IReadOnlyList<ViewNode> Build(InventoryBlock block, InventoryRecord record, string lazyId,
        Func<string, string, FieldSpec, FieldValue, FieldNode> bind)
    {
        if (record.Upgrades is not { } upgrades) return [new TextNode("Unsupported upgrade layout; original bytes are preserved.")];
        var rowKey = $"upgrades/{block.Key}/{record.Key}";
        var fields = upgrades.Grid.Select((mask, index) => bind($"{rowKey}/ModGrid row/{index}",
            $"Stored row {index}", IntegerSpec.UInt64, FieldValue.Of((ulong)mask)) with
        {
            Confidence = Confidence.Inferred,
            Description = "Full-mask schema slot; PS5 byte order checked across the corpus. " + DdlSections.Attribution,
            Warning = "Read-only. Gameplay meaning and limits require independent HUD/action captures.",
        }).ToArray();
        var graph = new GraphNode("Stored weapon upgrade rows", fields.Select((field, index) =>
            new GraphItem($"row/{index}", field.Label, GraphState.Unknown)
            {
                Branch = "ModGrid",
                Description = $"Stored mask 0x{upgrades.Grid[index].ToString("X4", CultureInfo.InvariantCulture)}. " + Boundary,
                Target = new(ViewTargetKind.Field, field.ValueId)
                {
                    Path = ["tab:Upgrades", $"row:{rowKey}", $"section:{lazyId}", "section:Stored row masks"],
                },
            }).ToArray(), [], Boundary + DdlSections.Attribution);
        return [graph, new SectionNode("Stored row masks", fields)];
    }
}
