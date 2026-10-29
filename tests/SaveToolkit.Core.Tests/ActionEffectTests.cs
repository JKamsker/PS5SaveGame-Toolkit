using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class ActionEffectTests : IDisposable
{
    private readonly EffectPlugin _plugin = new();
    private readonly WorkspaceStore _store;

    public ActionEffectTests() => _store = new(new PluginCatalog([_plugin]), new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
    public void Dispose() => _store.Dispose();

    [Fact]
    public void EffectPlan_IsDetachedAndLegacyNullIsOmitted()
    {
        var spec = new TextSpec { ReadOnly = true, MaxLength = 200 };
        var plan = new ActionPlan
        {
            Changes = [new PlannedChange("payload", "Payload", FieldValue.Null, FieldValue.Of("x"))
            {
                OldExists = false,
                Effect = new(ActionEffectKind.StructuralMemento, spec)
                {
                    Path = ["inventory", "payload"],
                    Presentation = new FieldPresentation { Confidence = Confidence.Inferred },
                },
            }],
        };
        var detached = ActionPlanSnapshots.Snapshot(plan);
        var json = JsonSerializer.Serialize(new ActionPlan { Changes = [new("a", "A", FieldValue.Of(1), FieldValue.Of(2))] });
        Assert.DoesNotContain("effect", json, StringComparison.Ordinal);
        Assert.True(ActionPlans.ContentEquals(plan, detached));
        Assert.Equal(spec, detached.Changes[0].Effect!.Spec);
        Assert.NotSame(plan.Changes[0].Effect!.Path, detached.Changes[0].Effect!.Path);
    }

    [Fact]
    public void ObservationEffect_UsesOneWholeActionUndoAndRedo()
    {
        using var session = Open();
        var preview = session.PlanAction("toggle", new Dictionary<string, JsonElement>());
        Assert.NotNull(preview.PlanId);
        Assert.Equal(ActionEffectKind.Observation, preview.Plan.Changes.Single().Effect!.Kind);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(1, _plugin.Opened!.Value);
        Assert.True(session.State.CanUndo);
        Assert.True(session.Undo().Ok);
        Assert.Equal(0, _plugin.Opened!.Value);
        Assert.True(session.Redo().Ok);
        Assert.Equal(1, _plugin.Opened!.Value);
    }

    private DocumentSession Open()
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 0 }));
        var workspace = _store.Create("effect", [new ImportSource("save.zip", () => new MemoryStream(zip))]);
        var file = workspace.Set.AllDiskFiles.Single();
        var session = new DocumentSession("effect", file, _plugin, _plugin.Open(file), openOriginal: () => new EffectDocument());
        _ = session.BuildView();
        return session;
    }

    private sealed class EffectPlugin : IGamePlugin
    {
        public PluginInfo Info { get; } = new("test.effects", "Effects", "1.0.0");
        public EffectDocument? Opened { get; private set; }
        public ProbeResult Probe(ISaveFile file) => ProbeResult.Match(ProbeScore.Format, "Effects");
        public ISaveDocument Open(ISaveFile file) => Opened = new EffectDocument();
    }

    private sealed class EffectDocument : BoundSaveDocument, IActionEffectDocument
    {
        public int Value { get; private set; }
        public override string Title => "Effects";
        public override ViewNode BuildView() => new SectionNode("Save", [new ActionNode("toggle", "Toggle") { SupportsPlanning = true }]);
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds) => [];
        public DocumentMemento CaptureMemento() => CaptureSnapshot()!;

        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
            new()
            {
                Changes = [new PlannedChange("payload", "Action payload", FieldValue.Of(Value), FieldValue.Of(Value + 1))
                {
                    Effect = new(ActionEffectKind.Observation, new IntegerSpec(0, 10) { ReadOnly = true }),
                }],
            };

        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
        {
            var before = CaptureSnapshot()!;
            Value++;
            return new EditOutcome
            {
                Changes = [new ValueChange("payload", FieldValue.Of(Value - 1), FieldValue.Of(Value))],
                StructureChanged = true,
                Revert = before,
            };
        }

        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new[] { (byte)Value })];
        protected override DocumentMemento CaptureSnapshot() => DocumentMemento.Snapshot(Value, 8);
        protected override void LoadSnapshot(DocumentMemento snapshot) => Value = ((SnapshotMemento<int>)snapshot).State;
    }
}
