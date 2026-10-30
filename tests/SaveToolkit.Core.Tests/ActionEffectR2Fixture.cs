using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

internal sealed class ActionEffectR2Fixture : IDisposable
{
    private readonly WorkspaceStore _store;
    public ActionEffectR2Document Document { get; } = new();
    public DocumentSession Session { get; }
    public ActionEffectR2Fixture()
    {
        var plugin = new ActionEffectR2Plugin(Document);
        _store = new(new PluginCatalog([plugin]), new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
        var bytes = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 0 }));
        var workspace = _store.Create("effect-r2", [new ImportSource("save.zip", () => new MemoryStream(bytes))]);
        Session = new("effect-r2", workspace.Set.AllDiskFiles.Single(), plugin, Document,
            new DocumentSessionOptions { MaxSnapshotBytes = 1024 }, openOriginal: () => new ActionEffectR2Document());
        _ = Session.BuildView();
    }
    public void Dispose() { Session.Dispose(); _store.Dispose(); }
}

internal sealed class ActionEffectR2Plugin(ActionEffectR2Document? document = null) : IGamePlugin
{
    public PluginInfo Info { get; } = new("test.effect-r2", "Effect R2", "1.0.0");
    public ProbeResult Probe(ISaveFile file) => ProbeResult.Match(ProbeScore.Format, "Effect R2");
    public ISaveDocument Open(ISaveFile file) => document ?? new ActionEffectR2Document();
}

internal sealed class ActionEffectR2Document : BoundSaveDocument, IActionEffectDocument, IPresetDocument
{
    public int Value { get; private set; }
    public int RestoreCalls { get; private set; }
    public int CaptureCalls { get; private set; }
    public int WriterCalls { get; private set; }
    public int ResolverCalls { get; private set; }
    public bool OversizedInverse { get; set; }
    public bool FailRebindAfterRestore { get; set; }
    public bool FailRestore { get; set; }
    public int PresetValue { get; private set; }
    public int CapabilityReads { get; private set; }
    public int PresetResolveCalls { get; private set; }
    public int PresetPlanCalls { get; private set; }
    public int PresetApplyCalls { get; private set; }
    public PresetCapability PresetCapability
    {
        get { CapabilityReads++; return new("1", null, [new("set", "Set", [])]); }
    }
    public FieldSpec EffectSpec { get; set; } = IntegerSpec.Int32 with { ReadOnly = true };
    public Func<ActionPlan, EditOutcome>? Outcome { get; set; }
    public Func<ActionPlan>? Preview { get; set; }
    public override string Title => "Effect R2";
    public override ViewNode BuildView()
    {
        if (FailRebindAfterRestore && RestoreCalls > 0) throw new InvalidOperationException("fresh model binding failed");
        return new SectionNode(Title,
        [
            Field("live", "Live", IntegerSpec.Int32, () => FieldValue.Of(Value), value => Value = value.AsInt32()),
            Field("preset", "Preset", IntegerSpec.Int32, () => FieldValue.Of(PresetValue), value => PresetValue = value.AsInt32()),
            new ActionNode("change", "Change") { SupportsPlanning = true },
        ]);
    }
    public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> ids) { ResolverCalls++; return []; }
    public DocumentMemento CaptureMemento() { CaptureCalls++; return new RelativeSnapshot(Value, 8); }
    public override DocumentMemento RestoreMemento(DocumentMemento token)
    {
        RestoreCalls++;
        if (FailRestore) throw new InvalidOperationException("standalone restore failed");
        var snapshot = (RelativeSnapshot)token;
        var inverse = new RelativeSnapshot(Value, OversizedInverse ? 2048 : 8);
        // This legal consumed-token model exposes double rollback: applying the same inverse
        // operation twice changes state twice. New inverse tokens still support normal redo.
        snapshot.Delta ??= snapshot.Target - Value;
        Value += snapshot.Delta.Value;
        return inverse;
    }
    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) => Preview?.Invoke() ?? new()
    {
        Changes = [new PlannedChange("effect", "Effect", FieldValue.Of(Value), FieldValue.Of(Value + 1))
        {
            Effect = new(ActionEffectKind.Observation, EffectSpec),
        }],
    };
    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        WriterCalls++;
        Value++;
        return Outcome?.Invoke(plan) ?? new EditOutcome
        {
            Changes = plan.Changes.Select(change => new ValueChange(change.ValueId, change.OldValue, change.NewValue)).ToArray(),
        };
    }
    public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)Value })];
    public PresetResolution ResolvePresetInstances(IReadOnlyList<PresetInstanceReference> instances)
    {
        PresetResolveCalls++;
        return new(instances.Select(instance => new PresetInstanceResolution(instance,
            PresetInstanceResolutionStatus.Resolved, [instance])).ToArray());
    }
    public ActionPlan PlanPreset(IReadOnlyList<PresetAction> actions, PresetResolution resolution)
    {
        PresetPlanCalls++;
        return new() { Changes = [new("preset", "Preset", FieldValue.Of(PresetValue), FieldValue.Of(9))] };
    }
    public EditOutcome ApplyPreset(IReadOnlyList<PresetAction> actions, PresetResolution resolution, ActionPlan plan)
    {
        PresetApplyCalls++;
        return ApplyEdits([new ValueEdit("preset", FieldValue.Of(9))]);
    }
    private sealed class RelativeSnapshot(int target, long size) : DocumentMemento
    {
        public int Target { get; } = target;
        public int? Delta { get; set; }
        public override long SizeBytes => size;
    }
}
