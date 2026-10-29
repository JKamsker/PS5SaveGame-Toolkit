using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class FieldMetadataR1Tests : IDisposable
{
    private readonly WorkspaceStore _store = new(new PluginCatalog([]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    [Fact]
    public void RepeatedPrimaryEffectsReplayEarliestOldAndLatestNewWithDerivedOutput()
    {
        using var document = new ReplayDocument();
        using var session = Open(document, "replay");
        _ = session.BuildView();
        var before = document.Serialize().Single().Content.ToArray();

        Assert.True(session.ApplyEdits([new IncomingEdit("p", JsonSerializer.SerializeToElement(3))]).Ok);
        var changed = document.Serialize().Single().Content.ToArray();
        Assert.Equal([3], changed);
        Assert.True(session.Undo().Ok);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(changed, document.Serialize().Single().Content.ToArray());

        using var actionDocument = new ReplayDocument();
        using var actionSession = Open(actionDocument, "action");
        _ = actionSession.BuildView();
        Assert.True(session.State.Revision > 0);
        var actionResult = actionSession.InvokeAction("repeat", new Dictionary<string, JsonElement>());
        if (!actionResult.Ok)
            throw new Xunit.Sdk.XunitException(JsonSerializer.Serialize(actionResult));
        var actionBytes = actionDocument.Serialize().Single().Content.ToArray();
        Assert.True(actionSession.Undo().Ok);
        Assert.Equal([1], actionDocument.Serialize().Single().Content.ToArray());
        Assert.True(actionSession.Redo().Ok);
        Assert.Equal(actionBytes, actionDocument.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void ReadOnlyPrimaryCannotBeAnAliasTarget()
    {
        using var document = new ReadOnlyAliasDocument();
        Assert.Throws<InvalidOperationException>(() => document.ResolveFieldMetadata(["alias"]));
        Assert.Throws<InvalidOperationException>(() => new ViewIndex(document).Metadata("alias"));
    }

    [Fact]
    public void MetadataIndexPublishesNoEarlierBatchWhenLaterBatchFails()
    {
        using var document = new BatchDocument();
        var index = new ViewIndex(document);
        var ids = Enumerable.Range(0, 200).Select(i => $"f{i}").Append("fail").ToArray();

        Assert.Throws<InvalidOperationException>(() => index.ResolveMetadata(ids));
        Assert.DoesNotContain("f0", index.BindingIds);
        Assert.DoesNotContain("f199", index.BindingIds);
    }

    [Fact]
    public void EmptyAndExplicitSelfCanonicalIdsDoNotRetireAPlan()
    {
        using var document = new ToggleCanonicalDocument();
        using var session = Open(document, "toggle");
        _ = session.BuildView();
        var preview = session.PlanAction("set", new Dictionary<string, JsonElement>());

        if (preview.PlanId is null)
            throw new Xunit.Sdk.XunitException(JsonSerializer.Serialize(preview));
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(3, document.Value);
    }

    [Fact]
    public void TargetedCaptureCancellationRestoresTheWholeMutableBaseline()
    {
        using var cancellation = new CancellationTokenSource();
        var originalOpens = 0;
        var current = new CancellationDocument(cancellation, cancelAfterFirst: false);
        using var session = Open(current, "cancel-targeted", () =>
        {
            originalOpens++;
            return new CancellationDocument(cancellation, originalOpens > 1);
        });
        _ = session.BuildView();
        Assert.Empty(session.GetChanges().Changes);

        Assert.Throws<OperationCanceledException>(() => session.ApplyEdits([
            new IncomingEdit("hidden-1", JsonSerializer.SerializeToElement(2)),
            new IncomingEdit("hidden-2", JsonSerializer.SerializeToElement(3)),
        ], expectedRevision: null, cancellationToken: cancellation.Token));

        Assert.Equal(1, current.Hidden1);
        Assert.Equal(1, current.Hidden2);
        var after = session.GetChanges();
        Assert.True(after.Complete);
        Assert.Empty(after.Changes);
    }

    [Fact]
    public void MetadataDriftAfterApplyProducesIncompleteReviewWithoutRevertAuthority()
    {
        var document = new DriftingMetadataDocument();
        using var session = Open(document, "drift", () => new DriftingMetadataDocument());
        _ = session.BuildView();

        Assert.True(session.ApplyEdits([new IncomingEdit("hidden", JsonSerializer.SerializeToElement(2))]).Ok);
        document.Drift = true;

        var review = session.GetChanges();
        Assert.False(review.Complete);
        Assert.Empty(review.Changes);
        Assert.Contains(review.Warnings, warning => warning.Contains("Targeted metadata coverage", StringComparison.Ordinal));
    }

    private DocumentSession Open(ISaveDocument document, string name, Func<ISaveDocument>? openOriginal = null) =>
        new(name, _store.Create(name, [new ImportSource("save.dat", () => new MemoryStream(new byte[] { 1 }))])
            .Set.AllDiskFiles.Single(), new CounterPlugin(), document, openOriginal: openOriginal);

    private sealed class ReplayDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        public int Value { get; private set; } = 1;
        public override string Title => "Replay";

        public ReplayDocument()
        {
            RegisterField(new FieldMetadata("p", "Primary", new IntegerSpec(0, 100)),
                () => FieldValue.Of(Value), value => Value = value.AsInt32());
            RegisterField(new FieldMetadata("d", "Derived", new IntegerSpec(0, 100) { ReadOnly = true })
                { BindingKind = FieldBindingKind.Derived, CanonicalValueId = "p" },
                () => FieldValue.Of(Value * 2));
        }

        public override ViewNode BuildView() => new SectionNode("Replay", [new ActionNode("repeat", "Repeat")]);
        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)Value })];

        public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
        {
            var before = Value;
            var outcome = base.ApplyEdits(edits);
            return !outcome.Succeeded ? outcome : outcome with
            {
                Changes = [new("p", FieldValue.Of(before), FieldValue.Of(before + 1)),
                    new("p", FieldValue.Of(before + 1), FieldValue.Of(Value)),
                    new("d", FieldValue.Of(before * 2), FieldValue.Of(Value * 2))],
            };
        }

        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
        {
            var before = Value;
            Value = 3;
            return new ActionOutcome
            {
                Changes = [new("p", FieldValue.Of(before), FieldValue.Of(2)),
                    new("p", FieldValue.Of(2), FieldValue.Of(Value)),
                    new("d", FieldValue.Of(before * 2), FieldValue.Of(Value * 2))],
            };
        }
    }

    private sealed class ReadOnlyAliasDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        public ReadOnlyAliasDocument()
        {
            RegisterField(new FieldMetadata("primary", "Primary", IntegerSpec.Int32 with { ReadOnly = true }),
                () => FieldValue.Of(1), _ => { });
            RegisterField(new FieldMetadata("alias", "Alias", IntegerSpec.Int32 with { ReadOnly = true })
                { BindingKind = FieldBindingKind.Derived, CanonicalValueId = "primary" },
                () => FieldValue.Of(1));
        }

        public override string Title => "Read only alias";
        public override ViewNode BuildView() => new TextNode("empty");
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }

    private sealed class BatchDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        public override string Title => "Batch";
        public override ViewNode BuildView() => new TextNode("empty");
        public override IReadOnlyList<FileOutput> Serialize() => [];

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            if (valueIds.Contains("fail", StringComparer.Ordinal)) throw new InvalidOperationException("later batch failed");
            foreach (var id in valueIds)
                RegisterField(new FieldMetadata(id, id, IntegerSpec.Int32), () => FieldValue.Of(1), _ => { });
            return base.ResolveFieldMetadata(valueIds);
        }
    }

    private sealed class ToggleCanonicalDocument : BoundSaveDocument, IFieldMetadataDocument, IActionPlanningDocument
    {
        private bool _explicit;
        public int Value { get; private set; } = 2;
        public ToggleCanonicalDocument() => RegisterField(new FieldMetadata("p", "Primary", IntegerSpec.Int32),
            () => FieldValue.Of(Value), value => Value = value.AsInt32());
        public override string Title => "Toggle canonical";
        public override ViewNode BuildView() => new SectionNode("Toggle", [new ActionNode("set", "Set") { SupportsPlanning = true }]);
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            var fields = base.ResolveFieldMetadata(valueIds);
            _explicit = !_explicit;
            return fields.Select(field => field with { CanonicalValueId = _explicit ? field.ValueId : string.Empty }).ToArray();
        }
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
            new() { Changes = [new PlannedChange("p", "Primary", FieldValue.Of(Value), FieldValue.Of(3))] };
        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
            ActionScope scope, ActionPlan plan) => ApplyEdits([new ValueEdit("p", FieldValue.Of(3))]);
    }

    private sealed class CancellationDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly bool _cancelAfterFirst;
        public int Hidden1 { get; private set; } = 1;
        public int Hidden2 { get; private set; } = 1;

        public CancellationDocument(CancellationTokenSource cancellation, bool cancelAfterFirst)
        {
            _cancellation = cancellation;
            _cancelAfterFirst = cancelAfterFirst;
            RegisterField(new FieldMetadata("visible", "Visible", IntegerSpec.Int32), () => FieldValue.Of(1), _ => { });
            RegisterField(new FieldMetadata("hidden-1", "Hidden one", IntegerSpec.Int32),
                () => FieldValue.Of(Hidden1), value => Hidden1 = value.AsInt32());
            RegisterField(new FieldMetadata("hidden-2", "Hidden two", IntegerSpec.Int32),
                () => FieldValue.Of(Hidden2), value => Hidden2 = value.AsInt32());
        }

        public override string Title => "Targeted cancellation";
        public override ViewNode BuildView() => new SectionNode("Visible", [Field("visible", "Visible")]);
        public override IReadOnlyList<FileOutput> Serialize() => [];

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            var fields = base.ResolveFieldMetadata(valueIds);
            return _cancelAfterFirst ? new CancellingMetadataList(fields, _cancellation) : fields;
        }
    }

    private sealed class DriftingMetadataDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private int _value = 1;
        public bool Drift { get; set; }

        public DriftingMetadataDocument() => RegisterField(new FieldMetadata("hidden", "Hidden", IntegerSpec.Int32),
            () => FieldValue.Of(_value), value => _value = value.AsInt32());

        public override string Title => "Drifting metadata";
        public override ViewNode BuildView() => new TextNode("No rendered fields");
        public override IReadOnlyList<FileOutput> Serialize() => [];

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            var fields = base.ResolveFieldMetadata(valueIds);
            return Drift ? fields.Select(field => field with
            {
                BindingKind = FieldBindingKind.Primary,
                Spec = field.Spec with { ReadOnly = true },
            }).ToArray() : fields;
        }
    }

    private sealed class CancellingMetadataList(IReadOnlyList<FieldMetadata> fields, CancellationTokenSource cancellation)
        : IReadOnlyList<FieldMetadata>
    {
        public int Count => fields.Count;
        public FieldMetadata this[int index] => fields[index];
        public IEnumerator<FieldMetadata> GetEnumerator()
        {
            for (var index = 0; index < fields.Count; index++)
            {
                if (index == 1) cancellation.Cancel();
                yield return fields[index];
            }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
