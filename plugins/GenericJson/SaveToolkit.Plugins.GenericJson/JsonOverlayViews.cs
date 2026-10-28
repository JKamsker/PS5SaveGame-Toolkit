using System.Globalization;
using System.Text.Json;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>File-derived descriptions and planned adapters. Raw keys/paths and encodings remain visible.</summary>
internal static class JsonOverlayViews
{
    private const int MaxDisplayValue = 4096;

    public static IReadOnlyList<ViewNode> Build(JsonOverlay overlay, Func<string, JsonValueToken, SectionNode> lazy,
        Func<JsonValueToken, IReadOnlyList<ViewNode>> controls)
    {
        if (!overlay.IsProfile) return overlay.Groups.Select(node => Create(node, lazy, controls)).ToArray();
        var chapters = overlay.Groups.Where(node => !node.Label.StartsWith("Minigame", StringComparison.Ordinal)).ToArray();
        var games = overlay.Groups.Except(chapters).GroupBy(node => GameAndField(node.Label).Game, StringComparer.Ordinal);
        return [new SectionNode("Chapter and checkpoint strings", chapters.Select(node => Create(node, lazy, controls)).ToArray())
            { Description = JsonOverlay.Provenance },
            .. games.Select(group => new SectionNode(JsonLabels.Friendly(group.Key), group.Select(node =>
                Scalar(node, JsonLabels.Friendly(GameAndField(node.Label).Field), controls)).ToArray())
            {
                Description = "Only present fields are listed; missing discovery, play and counter keys remain unknown. " + JsonOverlay.Provenance,
            })];
    }

    public static ViewNode Create(JsonValueToken node, Func<string, JsonValueToken, SectionNode> lazy,
        Func<JsonValueToken, IReadOnlyList<ViewNode>> controls)
    {
        if (!node.IsContainer) return Scalar(node, JsonLabels.Friendly(node.Label), controls);
        if (JsonOverlay.HasType(JsonOverlay.Unique(node, "group_name"), JsonScalar.Text)
            && JsonOverlay.Unique(node, "options") is { Kind: JsonTokenType.StartArray } options)
        {
            var raw = JsonOverlay.Unique(node, "group_name")!.Current.ToString();
            return new SectionNode(JsonLabels.Friendly(raw), [new TextNode(raw) { Monospace = true }, lazy("Recorded options", options)])
            { Description = JsonOverlay.Provenance };
        }
        if (JsonOverlay.HasType(JsonOverlay.Unique(node, "name"), JsonScalar.Text) && JsonOverlay.Unique(node, "value") is { IsContainer: false } value)
            return Option(node, value, lazy, controls);
        return lazy(JsonLabels.Friendly(node.Label), node) with { Description = JsonOverlay.Provenance };
    }

    private static ViewNode Option(JsonValueToken option, JsonValueToken value, Func<string, JsonValueToken, SectionNode> lazy,
        Func<JsonValueToken, IReadOnlyList<ViewNode>> controls)
    {
        var name = JsonOverlay.Unique(option, "name")!.Current.ToString();
        var children = new List<ViewNode>
        {
            new TextNode($"Raw name: {name}; {JsonPaths.ForNode(option)}") { Monospace = true },
            Scalar(value, "Current JSON value", controls),
        };
        foreach (var key in new[] { "type", "default_value", "min_value", "max_value", "step_value", "index", "default_index", "is_dynamic" })
            if (JsonOverlay.Unique(option, key) is { IsContainer: false } metadata) children.Add(Scalar(metadata, "Recorded " + JsonLabels.Friendly(key)));
        if (JsonOverlay.Unique(option, "values") is { Kind: JsonTokenType.StartArray } values)
        {
            children.Add(new TextNode(IndexStatus(option, value, values)) { Tone = Tone.Info });
            children.Add(lazy("Recorded choices (raw JSON)", values));
        }
        else children.Add(new TextNode("No recorded choice list; any dynamic choices remain unknown.") { Tone = Tone.Muted });
        // Complete metadata remains in the raw JSON tab. Republishing the same choice lazy range
        // here would create duplicate traversal paths and make Core's original-value review incomplete.
        return new SectionNode(JsonLabels.Friendly(name), children)
        {
            Collapsed = true, Description = $"Raw option {name}; {JsonPaths.ForNode(option)}. {JsonOverlay.Provenance}",
        };
    }

    private static string IndexStatus(JsonValueToken option, JsonValueToken value, JsonValueToken choices)
    {
        var index = JsonOverlay.Unique(option, "index");
        if (!JsonOverlay.HasType(index, JsonScalar.Number)
            || !int.TryParse(index!.Current.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var position)
            || position >= choices.Children.Count || choices.Children[position].IsContainer)
            return "Index consistency: no unambiguous in-range integer index. This is not a validated choice schema.";
        return choices.Children[position].Current == value.Current
            ? "Index consistency: current value exactly matches the recorded choice. Coupled write behavior remains unproven."
            : "Index consistency: current value does not exactly match the indexed choice. Do not infer a repair.";
    }

    private static ViewNode Scalar(JsonValueToken node, string label, Func<JsonValueToken, IReadOnlyList<ViewNode>>? controls = null)
    {
        var literal = JsonScalar.Literal(node.Current);
        if (literal.Length > MaxDisplayValue) literal = literal[..MaxDisplayValue] + "… (use raw JSON for the complete value)";
        return new SectionNode(label, [new TextNode(literal) { Monospace = true }, .. controls?.Invoke(node) ?? []])
        {
            Description = $"{node.Label}; {JsonPaths.ForNode(node)}. {JsonOverlay.Provenance} Stored JSON strings are shown quoted without coercion.",
        };
    }

    internal static (string Game, string Field) GameAndField(string label)
    {
        var dot = label.IndexOf('.');
        var name = label[(dot + 1)..];
        if (label.StartsWith("Minigame.", StringComparison.Ordinal)) return (name, "Raw minigame value");
        var separator = name.LastIndexOf('_');
        return separator < 0 ? (name, label[..dot]) : (name[..separator], name[(separator + 1)..]);
    }
}
