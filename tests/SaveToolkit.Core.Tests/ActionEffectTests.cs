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
        var legacy = new ActionPlan { Changes = [new("legacy/" + new string('x', 1_800), new string('l', 1_500), FieldValue.Of(1), FieldValue.Of(2))] };
        Assert.Single(ActionPlanSnapshots.Snapshot(legacy).Changes);
    }

    [Fact]
    public void NestedSpecAndPathSnapshotsCompareByStructureAfterProviderMutation()
    {
        var options = new List<UnionOption> { new("count", "Count", IntegerSpec.Int32) };
        var spec = new UnionSpec(options) { ReadOnly = true };
        var plan = new ActionPlan
        {
            Changes = [new PlannedChange("payload", "Payload", FieldValue.Null, FieldValue.Union("count", FieldValue.Of(1)))
            {
                OldExists = false,
                Effect = new(ActionEffectKind.StructuralMemento, spec) { Path = ["items", "0"] },
            }],
        };
        var detached = ActionPlanSnapshots.Snapshot(plan);
        options[0] = new("other", "Other", new TextSpec { ReadOnly = true });
        Assert.Equal("count", Assert.IsType<UnionSpec>(detached.Changes[0].Effect!.Spec).Options[0].Id);
        var equivalent = plan with
        {
            Changes = [plan.Changes[0] with
            {
                Effect = plan.Changes[0].Effect! with
                {
                    Spec = new UnionSpec([new("count", "Count", IntegerSpec.Int32)]) { ReadOnly = true },
                    Path = ["items", "0"],
                },
            }],
        };
        Assert.True(ActionPlans.ContentEquals(detached, ActionPlanSnapshots.Snapshot(equivalent)));
        Assert.False(ActionPlans.ContentEquals(detached, detached with
        {
            Changes = [detached.Changes[0] with { Effect = detached.Changes[0].Effect! with { Path = ["items", "1"] } }],
        }));
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

    [Fact]
    public void ObservationEffectMayUseHostMementoWithoutProviderStructuralToken()
    {
        using var session = Open();
        _plugin.Opened!.Mode = EffectPlanMode.ObservationNoToken;
        var preview = session.PlanAction("toggle", new Dictionary<string, JsonElement>());
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.True(session.State.CanUndo);
        Assert.False(session.GetChanges().Complete);
        Assert.Contains(session.GetChanges().Warnings, warning => warning.Contains("Action-owned effects", StringComparison.Ordinal));
        Assert.True(session.Undo().Ok);
        Assert.True(session.Redo().Ok);
    }

    [Fact]
    public void CanonicalFirstRejectsShadowAndResolverFailureNeverMeansEffectAbsence()
    {
        using var session = Open();
        _plugin.Opened!.Mode = EffectPlanMode.CanonicalShadow;
        Assert.Null(session.PlanAction("toggle", new Dictionary<string, JsonElement>()).PlanId);
        _plugin.Opened.Mode = EffectPlanMode.ResolverFailure;
        var failed = session.PlanAction("toggle", new Dictionary<string, JsonElement>());
        Assert.Contains(failed.Plan.Errors, error => error.Message.Contains("metadata", StringComparison.OrdinalIgnoreCase));

        _plugin.Opened.Mode = EffectPlanMode.CanonicalOmission;
        var omission = session.PlanAction("toggle", new Dictionary<string, JsonElement>());
        Assert.Null(omission.PlanId);
        Assert.Contains(omission.Plan.Errors, error => error.Message.Contains("shadow", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MalformedEffectOutcomesRestoreBytesAndDoNotRecordHistory()
    {
        using var session = Open();
        foreach (var mode in new[] { EffectPlanMode.MissingRevert, EffectPlanMode.WrongOutcome,
            EffectPlanMode.AfterSnapshotTooLarge, EffectPlanMode.ThrowAfterMutation })
        {
            _plugin.Opened!.Mode = mode;
            var preview = session.PlanAction("toggle", new Dictionary<string, JsonElement>());
            Assert.NotNull(preview.PlanId);
            Assert.False(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
            Assert.Equal(0, _plugin.Opened.Value);
            Assert.Equal(new byte[] { 0 }, _plugin.Opened.Serialize().Single().Content.ToArray());
            Assert.False(session.State.CanUndo);
            Assert.Equal(FieldValue.Of(0), _plugin.Opened.GetValue("live"));
        }
    }

    [Fact]
    public void BrokenRestoreSurfacesExplicitUnusableStateFailure()
    {
        using var session = Open();
        _plugin.Opened!.Mode = EffectPlanMode.BrokenRestore;
        var preview = session.PlanAction("toggle", new Dictionary<string, JsonElement>());
        Assert.NotNull(preview.PlanId);

        var result = session.ApplyActionPlan(preview.PlanId!, preview.Revision);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Message.Contains("could not restore", StringComparison.OrdinalIgnoreCase));
        Assert.False(session.CanEdit);
        Assert.False(session.ApplyEdits([new IncomingEdit("live", JsonSerializer.SerializeToElement(2))]).Ok);
        Assert.Throws<InvalidOperationException>(() => session.Serialize());
    }

    [Fact]
    public void MixedCanonicalAndEffectRowsPreserveOrderAndNullAbsence()
    {
        using var session = Open();
        _plugin.Opened!.Mode = EffectPlanMode.Mixed;
        var preview = session.PlanAction("toggle", new Dictionary<string, JsonElement>());
        Assert.Equal(["live", "payload"], preview.Plan.Changes.Select(change => change.ValueId));
        Assert.False(preview.Plan.Changes[1].OldExists);
        Assert.True(preview.Plan.Changes[1].OldValue.IsNull);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(1, _plugin.Opened.Value);
        Assert.True(session.Undo().Ok);
        Assert.Equal(0, _plugin.Opened.Value);
    }

    [Fact]
    public void EffectRowsNeverBecomeWritableFakeFields()
    {
        using var session = Open();
        var preview = session.PlanAction("toggle", new Dictionary<string, JsonElement>());
        var edit = session.ApplyEdits([new IncomingEdit("payload", JsonSerializer.SerializeToElement(9))]);
        Assert.False(edit.Ok);
        Assert.Contains(edit.Errors, error => error.ValueId == "payload");
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
    }

    [Fact]
    public void EffectRowsUseIndependentEntryAndByteBudgetsWithoutTruncation()
    {
        using var session = Open();
        _plugin.Opened!.Mode = EffectPlanMode.Bulk;
        _plugin.Opened.BulkCount = 5_001;
        var preview = session.PlanAction("toggle", new Dictionary<string, JsonElement>());
        Assert.NotNull(preview.PlanId);
        Assert.Equal(5_001, preview.Plan.Changes.Count);
        Assert.Equal("effect-5000", preview.Plan.Changes[^1].ValueId);

        _plugin.Opened.BulkCount = 10_002;
        var large = session.PlanAction("toggle", new Dictionary<string, JsonElement>());
        Assert.NotNull(large.PlanId);
        Assert.Equal(10_002, large.Plan.Changes.Count);

        _plugin.Opened.Mode = EffectPlanMode.OversizedSource;
        _plugin.Opened.ResolverCalls = 0;
        var oversized = session.PlanAction("toggle", new Dictionary<string, JsonElement>());
        Assert.Null(oversized.PlanId);
        Assert.Equal(0, _plugin.Opened.ResolverCalls);

        _plugin.Opened.Mode = EffectPlanMode.Bulk;
        _plugin.Opened.BulkCount = ActionPlanSnapshots.MaxPlanEntries + 1;
        _plugin.Opened.ResolverCalls = 0;
        var tooMany = session.PlanAction("toggle", new Dictionary<string, JsonElement>());
        Assert.Null(tooMany.PlanId);
        Assert.Equal(0, _plugin.Opened.ResolverCalls);
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
        public EffectPlanMode Mode { get; set; }
        public int BulkCount { get; set; }
        public int ResolverCalls { get; set; }
        public override string Title => "Effects";
        public override ViewNode BuildView() => new SectionNode("Save", [
            Field("live", "Live", IntegerSpec.Int32, () => FieldValue.Of(Value), value => Value = value.AsInt32()),
            new ActionNode("toggle", "Toggle") { SupportsPlanning = true },
        ]);
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            ResolverCalls++;
            if (Mode == EffectPlanMode.ResolverFailure) throw new InvalidOperationException("canonical metadata resolver failed");
            if (Mode == EffectPlanMode.CanonicalOmission) return [];
            return valueIds.Contains("live", StringComparer.Ordinal) ? [new("live", "Live", IntegerSpec.Int32)] : [];
        }
        public DocumentMemento CaptureMemento() => CaptureSnapshot()!;

        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
            new() { Changes = PlanChanges() };

        private IReadOnlyList<PlannedChange> PlanChanges() => Mode switch
        {
            EffectPlanMode.Bulk => Enumerable.Range(0, BulkCount).Select(index =>
                new PlannedChange($"effect-{index}", "Effect", FieldValue.Of(Value), FieldValue.Of(Value + 1))
                {
                    Effect = Effect(ActionEffectKind.Observation, FieldValue.Of(Value), FieldValue.Of(Value + 1)),
                }).ToArray(),
            EffectPlanMode.OversizedSource => [new PlannedChange("payload", "Payload", FieldValue.Of(Value), FieldValue.Of(Value + 1))
            {
                Effect = Effect(ActionEffectKind.Observation, FieldValue.Of(Value), FieldValue.Of(Value + 1)) with { Source = new string('x', 2_001) },
            }],
            EffectPlanMode.CanonicalShadow or EffectPlanMode.CanonicalOmission => [new PlannedChange("live", "Live", FieldValue.Of(Value), FieldValue.Of(Value + 1))
            {
                Effect = Effect(ActionEffectKind.Observation, FieldValue.Of(Value), FieldValue.Of(Value + 1)),
            }],
            EffectPlanMode.Mixed => [new("live", "Live", FieldValue.Of(Value), FieldValue.Of(Value + 1)),
                new PlannedChange("payload", "Action payload", FieldValue.Null, FieldValue.Of(Value + 1))
                {
                    OldExists = false,
                    Effect = Effect(ActionEffectKind.StructuralMemento, FieldValue.Null, FieldValue.Of(Value + 1)),
                }],
            EffectPlanMode.ObservationNoToken => [new PlannedChange("observed", "Observed", FieldValue.Of(Value), FieldValue.Of(Value + 1))
            {
                Effect = Effect(ActionEffectKind.Observation, FieldValue.Of(Value), FieldValue.Of(Value + 1)),
            }],
            _ => [new PlannedChange("payload", "Action payload", FieldValue.Of(Value), FieldValue.Of(Value + 1))
            {
                Effect = Effect(ActionEffectKind.Observation, FieldValue.Of(Value), FieldValue.Of(Value + 1)),
            }],
        };

        private static ActionEffectDescriptor Effect(ActionEffectKind kind, FieldValue oldValue, FieldValue newValue) =>
            new(kind, new IntegerSpec(0, 10) { ReadOnly = true });

        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
        {
            var before = CaptureSnapshot()!;
            Value++;
            if (Mode is EffectPlanMode.ThrowAfterMutation or EffectPlanMode.BrokenRestore)
                throw new InvalidOperationException("writer failed after mutation");
            if (Mode == EffectPlanMode.MissingRevert)
                return new EditOutcome { Changes = [new ValueChange("payload", FieldValue.Of(Value - 1), FieldValue.Of(Value))], StructureChanged = true };
            if (Mode == EffectPlanMode.WrongOutcome)
                return new EditOutcome { Changes = [new ValueChange("payload", FieldValue.Of(8), FieldValue.Of(9))], StructureChanged = true, Revert = before };
            if (Mode == EffectPlanMode.ObservationNoToken)
                return new EditOutcome
                {
                    Changes = plan.Changes.Select(change => new ValueChange(change.ValueId, change.OldValue, change.NewValue)).ToArray(),
                };
            return new EditOutcome
            {
                Changes = plan.Changes.Select(change => new ValueChange(change.ValueId, change.OldValue, change.NewValue)).ToArray(),
                StructureChanged = true,
                Revert = before,
            };
        }

        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new[] { (byte)Value })];
        protected override DocumentMemento CaptureSnapshot() => DocumentMemento.Snapshot(Value,
            Mode == EffectPlanMode.AfterSnapshotTooLarge && Value > 0 ? 512 * 1024 * 1024 : 8);
        protected override void LoadSnapshot(DocumentMemento snapshot)
        {
            if (Mode == EffectPlanMode.BrokenRestore)
                throw new InvalidOperationException("restore intentionally failed");
            Value = ((SnapshotMemento<int>)snapshot).State;
        }
    }

    private enum EffectPlanMode
    {
        Observation,
        CanonicalShadow,
        CanonicalOmission,
        ResolverFailure,
        MissingRevert,
        WrongOutcome,
        AfterSnapshotTooLarge,
        Mixed,
        Bulk,
        OversizedSource,
        ThrowAfterMutation,
        BrokenRestore,
        ObservationNoToken,
    }
}
