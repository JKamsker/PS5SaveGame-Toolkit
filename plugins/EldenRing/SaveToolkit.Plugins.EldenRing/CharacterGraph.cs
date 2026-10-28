using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing;

internal static class CharacterGraph
{
    public static GraphNode Build(int slot, IReadOnlyList<FieldNode> attributes)
    {
        var path = new[] { "tab:Characters", $"section:characters/{slot}" };
        var levelId = $"slots/{slot}/level";
        return new GraphNode("Attribute-to-level dependencies (read-only)",
        [
            .. attributes.Select(field => new GraphItem(field.ValueId, field.Label, GraphState.Unknown)
            {
                Branch = "Attributes",
                Description = $"Current value {field.Value.AsInteger()}. Arrow means contributes to the level equation, not an unlock or quest prerequisite. "
                    + "No class/modifier-aware respec writer is available. " + SemanticViews.Provenance,
                Target = new(ViewTargetKind.Field, field.ValueId) { Path = path },
            }),
            new GraphItem(levelId, "Level = sum of eight attributes − 79", GraphState.Unknown)
            {
                Branch = "Derived level",
                Description = "The parser validates the level equation and profile-level mirror. Effective HP/FP/stamina are not derived here. "
                    + SemanticViews.Provenance,
                Target = new(ViewTargetKind.Field, levelId) { Path = path },
            },
        ], attributes.Select(field => new GraphEdge(field.ValueId, levelId)).ToArray(),
            "Eight attribute fields feed the saved level: sum minus 79. All cards navigate to read-only character fields. "
            + "Unknown state means this is a calculation diagram, not a skill unlock, upgrade tree or quest graph. "
            + "Current attribute values are shown in descriptions; no vital reset, respec or gameplay change is performed.");
    }
}
