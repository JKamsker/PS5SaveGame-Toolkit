using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>Reference packs annotate recorded choices; the imported list remains the write authority.</summary>
internal sealed class JsonReferenceChoices(IReferenceContext references)
{
    private readonly Dictionary<string, IReferenceCatalog?> _catalogs = new(StringComparer.Ordinal);

    public JsonSemanticTarget Annotate(JsonValueToken option, JsonSemanticTarget target)
    {
        if (target.Choices is not { } choices || option.Parent?.Parent is not { } group) return target;
        var groupName = JsonOverlay.Unique(group, "group_name")?.Current.ToString();
        var optionName = JsonOverlay.Unique(option, "name")?.Current.ToString();
        var catalogId = (groupName, optionName) switch
        {
            ("/gameplay/difficulty", "GameDifficulty") => "cp2077-v136-difficulty",
            ("/video/display", "FrameRateModes") => "cp2077-v136-display",
            _ => null,
        };
        if (catalogId is null) return target;
        if (!_catalogs.TryGetValue(catalogId, out var catalog))
            _catalogs[catalogId] = catalog = references.Catalog(catalogId, "136");
        if (catalog is null) return target;
        var keys = choices.Select(v => v.ToString()).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length || keys.Any(k => catalog.Find(k) is null)) return target;
        var allowed = keys.ToHashSet(StringComparer.Ordinal);
        var raw = target.Node.Current.ToString();
        return target with
        {
            Spec = new ReferenceSpec(catalogId)
            {
                Assess = record => allowed.Contains(record.Key) ? new() : new ReferenceEligibility
                    { Eligible = false, Reasons = ["This ID is absent from the imported option's recorded choices."] },
            },
            Value = FieldValue.Of(raw),
            ChoiceKeys = keys,
            Encode = value => choices[System.Array.IndexOf(keys, value.AsString())],
            Validate = value => value.Kind == FieldValueKind.Text && allowed.Contains(value.AsString())
                ? null : "Choose an ID recorded for this option.",
            Evidence = target.Evidence + " " + (catalog.Find(raw)?.AttributionText ?? catalog.Find(keys[0])!.AttributionText),
        };
    }
}
