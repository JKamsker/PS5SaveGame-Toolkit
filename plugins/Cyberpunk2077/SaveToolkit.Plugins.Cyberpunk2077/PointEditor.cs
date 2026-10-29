using System.Collections.Immutable;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed class PointEditor(IReadOnlyList<PointBalance> points)
{
    public const string SetTargets = "progression/set-point-targets";
    public const string MappingSource = "https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Views/Controls/PlayerStatsControl.cs";
    private static readonly ImmutableArray<string> SupportedTypes = ["Attribute", "Primary", "Espionage"];
    private const int MaximumBalance = 100_000; // Existing editor policy, not a verified engine limit.
    private const string FieldPrefix = "progression/points/";
    private const string FieldWarning = WriteSafety.Warning +
        " Experimental: changes only unspent points. Spent points and unlocks are preserved; the editor cap is a policy.";
    private const string BatchWarning = WriteSafety.Warning +
        " Experimental / Inferred: sets independent unspent point balances, without budget arithmetic. " +
        "Spent points, attributes, perks and unlocks are preserved. Editor caps are policies, not verified gameplay limits.";
    private const string Sources = ScriptableReader.Source +
        " Point names: CP2077SaveEditor contributors (MIT), c1fd00305e7db905a4e7de8cfa360d4f4efee3b2. " + MappingSource;

    private static bool IsSupported(string type) => SupportedTypes.Contains(type);
    public bool CanEdit => points.Any(point => IsSupported(point.Type));

    public IReadOnlyList<ViewNode> Build(Func<ScalarBinding, FieldNode> bind)
    {
        var fields = points.Where(point => IsSupported(point.Type)).Select(point => Field(point, bind)).ToArray();
        if (fields.Length == 0) return [];
        return [.. fields, new ActionNode(SetTargets, "Set unspent point targets")
        {
            SupportsPlanning = true,
            Description = BatchWarning + " " + Sources,
            Parameters = fields.Select(field => field with
            {
                ValueId = field.ValueId[FieldPrefix.Length..],
                Description = "Explicit target for this existing balance. " + Sources,
                Warning = BatchWarning,
            }).ToArray(),
        }];
    }

    private static ScalarBinding Binding(PointBalance point)
    {
        var maximum = Math.Max(MaximumBalance, point.Original);
        var label = point.Type switch
        {
            "Attribute" => "Unspent attribute points",
            "Primary" => "Unspent perk points",
            _ => "Unspent relic points",
        };
        return new(FieldPrefix + point.Type, label, new IntegerSpec(0, maximum),
            () => FieldValue.Of(point.Value), value => point.Value = value.AsInt32(),
            value => value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > maximum
                ? $"Value must be an integer between 0 and {maximum}." : null);
    }

    private static FieldNode Field(PointBalance point, Func<ScalarBinding, FieldNode> bind) =>
        bind(Binding(point)) with { Warning = FieldWarning };

    public ActionPlan Plan(IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        ActionPlan Reject(string message) => new() { Errors = [new FieldError(SetTargets, message)], Warnings = [BatchWarning] };
        if (scope.QueryScope is not null || scope.RowKeys is { Count: > 0 })
            return Reject("Point targets require document scope; table and query selections are unsupported.");
        var supported = points.Where(point => IsSupported(point.Type)).ToArray();
        if (supported.Length == 0) return Reject("No supported existing point balances are editable.");
        if (parameters.Count != supported.Length || parameters.Keys.Any(key => !supported.Any(point => point.Type == key)))
            return Reject("Supply exactly one explicit target for each supported existing point balance.");
        foreach (var point in supported)
        {
            var binding = Binding(point);
            if (binding.Validate!(parameters[point.Type]) is { } error) return Reject($"{binding.Label}: {error}");
        }
        if (scope.RowKeys is { Count: 0 }) return new() { Warnings = [BatchWarning] };
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        foreach (var point in points)
        {
            if (!IsSupported(point.Type))
            {
                skipped.Add(new(point.Type, "Unsupported point type; existing record preserved."));
                continue;
            }
            var binding = Binding(point);
            var target = parameters[point.Type];
            if (binding.Get() == target)
                skipped.Add(new(point.Type, "Already at target; balance preserved."));
            else changes.Add(new(binding.Id, binding.Label, binding.Get(), target));
        }
        foreach (var type in SupportedTypes.Where(type => points.All(point => point.Type != type)))
            skipped.Add(new(type, "Point record is absent; no balance is created."));
        return new() { Changes = changes, SkippedRows = skipped, Warnings = [BatchWarning] };
    }

    public void BindTargets(ActionPlan plan, Func<ScalarBinding, FieldNode> bind)
    {
        var ids = plan.Changes.Select(change => change.ValueId).ToHashSet(StringComparer.Ordinal);
        foreach (var point in points.Where(point => IsSupported(point.Type) && ids.Contains(FieldPrefix + point.Type)))
            _ = Field(point, bind);
    }
}
