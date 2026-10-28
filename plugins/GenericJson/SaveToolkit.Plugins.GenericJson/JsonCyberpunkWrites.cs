using System.Globalization;
using System.Text.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson;

internal static class JsonCyberpunkWrites
{
    private const int MaxChoices = 128;

    public static JsonSemanticTarget? Create(JsonValueToken option)
    {
        try { return CreateCore(option); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            // A valid generic JSON document may contain malformed or unsupported title metadata.
            // Decline its semantic adapter while leaving the lossless workbench available.
            return null;
        }
    }

    private static JsonSemanticTarget? CreateCore(JsonValueToken option)
    {
        var value = JsonOverlay.Unique(option, "value");
        if (!JsonOverlay.HasType(JsonOverlay.Unique(option, "name"), JsonScalar.Text)
            || value is null || JsonSemanticValues.Primitive(value, false) is not { } target) return null;
        // Metadata edited through the raw workbench is not silently promoted to an authoritative schema.
        if (option.Children.Where(n => n.Label != "value" && n.Label != "index")
            .SelectMany(Descendants).Any(n => !n.Imported || n.Label != n.OriginalLabel || !n.IsContainer && n.Current != n.Original)) return null;
        var type = JsonOverlay.Unique(option, "type")?.Current.ToString();
        var choices = JsonOverlay.Unique(option, "values");
        var index = JsonOverlay.Unique(option, "index");
        if (choices is { Kind: JsonTokenType.StartArray } && choices.Children.Count is > 0 and <= MaxChoices
            && choices.Children.All(n => JsonOverlay.HasType(n, JsonScalar.TypeOf(value.Current)))
            && JsonOverlay.HasType(index, JsonScalar.Number))
        {
            var values = choices.Children.Select(n => n.Current).ToArray();
            var options = values.Select((v, i) => new EnumOption(i.ToString(CultureInfo.InvariantCulture), JsonScalar.Literal(v))).ToArray();
            var currentIndex = Array.IndexOf(values, value.Current);
            target = new(value, new EnumSpec(options), FieldValue.Of(currentIndex < 0 ? "" : currentIndex.ToString(CultureInfo.InvariantCulture)),
                v => values[int.Parse(v.AsString(), CultureInfo.InvariantCulture)],
                v => v.Kind == FieldValueKind.Text && options.Any(o => o.Value == v.AsString()) ? null : "Choose a recorded option.")
            { Index = index, Choices = values, Evidence = "Based on the file-recorded choice list. Value and index change together; their in-game authority is unconfirmed." };
        }
        else if (choices is not null) return null;
        else if (type is "int" or "float" && JsonOverlay.HasType(value, JsonScalar.Number))
        {
            var min = JsonOverlay.Unique(option, "min_value");
            var max = JsonOverlay.Unique(option, "max_value");
            if (!JsonOverlay.HasType(min, JsonScalar.Number) || !JsonOverlay.HasType(max, JsonScalar.Number)) return null;
            decimal? step = null;
            decimal origin = 0;
            if (JsonOverlay.Unique(option, "step_value") is { } increment)
            {
                if (!JsonSemanticValues.TryDecimal(increment.Current.ToString(), out var s) || s <= 0
                    || !JsonSemanticValues.TryDecimal(min!.Current.ToString(), out origin)) return null;
                // PS5 float steps are often rounded float32 metadata (e.g. 0.0099999997).
                // Exact modulo would reject the file's own defaults. Keep those steps advisory.
                if (type == "int") step = s;
            }
            target = JsonSemanticValues.Exact(value, new ExactNumberSpec { Min = min!.Current.ToString(), Max = max!.Current.ToString() }, type == "int", step, origin)
                with { Evidence = "Based on file-recorded min/max/step metadata for version 136. Integer steps allow recorded endpoints; "
                    + "float steps are advisory because recorded steps are approximate. PS5 acceptance and defaults are unverified." };
        }
        else if (index is not null)
        {
            if (!JsonOverlay.HasType(index, JsonScalar.Number) || !int.TryParse(index.Original.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out _)) return null;
            target = target with { Index = index, Confidence = SaveToolkit.Sdk.Views.Confidence.Unknown, Evidence = "Dynamic choice list missing: edit bounded raw text and explicit index only. "
                + "Index domain is limited to 0..imported index; no named choice or compatibility is inferred." };
        }
        return target with { Defaults = Defaults(option, target) };
    }

    private static IReadOnlyDictionary<string, FieldValue>? Defaults(JsonValueToken option, JsonSemanticTarget target)
    {
        if (target.Choices is { } choices)
        {
            var index = JsonOverlay.Unique(option, "default_index");
            if (index is null || !int.TryParse(index.Current.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var i) || i >= choices.Count) return null;
            return target.Changes(new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of(i.ToString(CultureInfo.InvariantCulture)) });
        }
        var value = JsonOverlay.Unique(option, "default_value");
        if (target.Index is not null || value is null || value.IsContainer || value.Current.IsNull
            || JsonScalar.TypeOf(value.Current) != JsonScalar.TypeOf(target.Node.Current)) return null;
        var parameter = value.Current.AsUnion().Value;
        return target.Validate(parameter) is null ? target.Changes(new Dictionary<string, FieldValue> { ["value"] = parameter }) : null;
    }

    private static IEnumerable<JsonValueToken> Descendants(JsonValueToken node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Descendants)) yield return child;
    }
}
