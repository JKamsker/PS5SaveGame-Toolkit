using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>A parameter adapter, never an alias binding: history uses the existing raw value IDs.</summary>
internal sealed record JsonSemanticTarget(JsonValueToken Node, FieldSpec Spec, FieldValue Value,
    Func<FieldValue, FieldValue> Encode, Func<FieldValue, string?> Validate)
{
    public JsonValueToken? Index { get; init; }
    public IReadOnlyList<FieldValue>? Choices { get; init; }
    public IReadOnlyDictionary<string, FieldValue>? Defaults { get; init; }
    public bool IsFlag { get; init; }
    public bool IsCounter { get; init; }
    public Confidence Confidence { get; init; } = Confidence.Inferred;
    public string Evidence { get; init; } = "Based on this imported field's type and conservative observed range; legal limits are unknown.";

    public string ActionId => $"semantic/{Node.Index}/set";
    public FieldNode Parameter(string key = "value") => new(key, JsonLabels.Friendly(Node.Label), Spec, Value)
    {
        Confidence = Confidence,
        Warning = JsonSemanticActions.Warning,
        Description = $"{JsonPaths.ForNode(Node)}. {Evidence} {JsonOverlay.Provenance} {JsonSemanticActions.Warning}",
    };

    public ActionNode View() => new(ActionId, "Edit existing " + JsonLabels.Friendly(Node.Label))
    {
        SupportsPlanning = true,
        Description = $"{Evidence} {JsonSemanticActions.Warning}",
        Parameters = Index is not null && Choices is null
            ? [Parameter(), new FieldNode("index", "Explicit raw index (domain unknown)",
                new IntegerSpec(0, JsonSemanticValues.IndexCeiling(Index)), FieldValue.Of(JsonSemanticValues.IndexCurrent(Index)))
                { Confidence = Confidence.Unknown, Warning = JsonSemanticActions.Warning, Description = JsonOverlay.Provenance + " " + JsonSemanticActions.Warning }]
            : [Parameter()],
    };

    public IReadOnlyDictionary<string, FieldValue> Changes(IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (!parameters.TryGetValue("value", out var value)) throw new ArgumentException("Supply a value.");
        if (Validate(value) is { } error) throw new ArgumentException(error);
        var result = new Dictionary<string, FieldValue>(StringComparer.Ordinal) { [Node.ValueId] = Encode(value) };
        if (Index is not null)
        {
            var position = Choices is not null ? int.Parse(value.AsString(), System.Globalization.CultureInfo.InvariantCulture)
                : JsonStructureActions.ReadIndex(parameters, "index", JsonSemanticValues.IndexCeiling(Index));
            result[Index.ValueId] = JsonSemanticValues.Number(position.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return result;
    }
}
