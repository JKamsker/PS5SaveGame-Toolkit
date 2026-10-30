using System.Text.Json;
using SaveToolkit.Core.Presets;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>A semantic adapter with observable callbacks for host ownership/transaction tests.</summary>
internal sealed class ControlledPresetDocument : BoundSaveDocument, IPresetDocument
{
    public const string Target = "target";
    public const string Candidate = "candidate-a";
    public const string OtherCandidate = "candidate-b";
    public bool Ambiguous { get; init; }
    public int Value { get; private set; }
    public int PlanCalls { get; private set; }
    public int ApplyCalls { get; private set; }
    public Func<IReadOnlyList<PresetInstanceReference>, IReadOnlyDictionary<string, string>, PresetResolution>? OnResolve { get; set; }
    public Action? OnPlan { get; set; }
    public Action? OnApply { get; set; }
    public override string Title => "Controlled preset";
    public PresetCapability PresetCapability => new("1", null, [new("set", "Set", [])]);

    public static ToolkitPreset Preset() => new("1", "fixture", "Fixture",
        new PresetCompatibility("test", "1", ["PPSA99999"]),
        [new PresetAction("set", new Dictionary<string, JsonElement>(), [new("owned", Target)])],
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    public override ViewNode BuildView() => new SectionNode(Title,
        [Field("value", "Value", IntegerSpec.Int32, () => FieldValue.Of(Value), value => Value = value.AsInt32())]);

    public PresetResolution ResolvePresetInstances(IReadOnlyList<PresetInstanceReference> instances) =>
        ResolvePresetInstances(instances, new Dictionary<string, string>());

    public PresetResolution ResolvePresetInstances(IReadOnlyList<PresetInstanceReference> instances,
        IReadOnlyDictionary<string, string> choices) => OnResolve?.Invoke(instances, choices) ?? Resolve(instances, choices);

    public PresetResolution Resolve(IReadOnlyList<PresetInstanceReference> instances, IReadOnlyDictionary<string, string> choices) =>
        new(instances.Select(instance => new PresetInstanceResolution(instance,
            Ambiguous && choices.Count == 0 ? PresetInstanceResolutionStatus.Ambiguous : PresetInstanceResolutionStatus.Resolved,
            Ambiguous && choices.Count == 0 ? [new("owned", Candidate), new("owned", OtherCandidate)]
                : [new("owned", choices.GetValueOrDefault(instance.StableId, Candidate))])).ToArray());

    public ActionPlan PlanPreset(IReadOnlyList<PresetAction> actions, PresetResolution resolution)
    {
        PlanCalls++;
        OnPlan?.Invoke();
        return new() { Changes = [new("value", "Value", FieldValue.Of(Value), FieldValue.Of(9))] };
    }

    public EditOutcome ApplyPreset(IReadOnlyList<PresetAction> actions, PresetResolution resolution, ActionPlan plan)
    {
        ApplyCalls++;
        OnApply?.Invoke();
        return ApplyEdits([new ValueEdit("value", FieldValue.Of(9))]);
    }

    public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)Value })];
}
