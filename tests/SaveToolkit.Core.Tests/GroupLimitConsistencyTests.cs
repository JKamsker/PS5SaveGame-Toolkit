using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Sessions.Groups;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class GroupLimitConsistencyTests
{
    [Theory]
    [InlineData(4097)]
    [InlineData(5001)]
    public void PreviewedGroupActionCanApplyUndoRedoAndReopenBeyondTheSemanticClosureLimit(int count)
    {
        using var fixture = new ActionEffectR2Fixture();
        var path = fixture.Session.File.RelativePath;
        var staged = new StagedGroupDocument(() => new BulkDocument(path, new byte[count]),
            candidate => candidate.Serialize(), () => { }, DocumentSessionOptions.Default.MaxSnapshotBytes);
        using var session = new DocumentSession("bulk-group", fixture.Session.File, fixture.Session.Plugin,
            staged, openOriginal: () => new BulkDocument(path, new byte[count]));
        _ = session.BuildView();

        var preview = session.PlanAction("fill", new Dictionary<string, JsonElement>());
        Assert.NotNull(preview.PlanId);
        Assert.Equal(count, preview.Plan.Changes.Count);
        var applied = session.ApplyActionPlan(preview.PlanId, preview.Revision);
        Assert.True(applied.Ok, string.Join("; ", applied.Errors.Select(error => error.Message)));
        Assert.All(staged.Serialize().Single().Content.ToArray(), value => Assert.Equal(1, value));

        Assert.True(session.Undo().Ok);
        Assert.All(staged.Serialize().Single().Content.ToArray(), value => Assert.Equal(0, value));
        Assert.True(session.Redo().Ok);
        var exported = staged.Serialize().Single().Content.ToArray();
        using var reopened = new BulkDocument(path, exported);
        Assert.Equal(count, exported.Length);
        Assert.All(reopened.Serialize().Single().Content.ToArray(), value => Assert.Equal(1, value));
    }

    private sealed class BulkDocument(string path, byte[] values) : BoundSaveDocument, IGroupedSaveDocument,
        IActionPlanningDocument, IFieldMetadataDocument
    {
        private byte[] _values = values.ToArray();
        public override string Title => "Bulk group";

        public override ViewNode BuildView() => new ActionNode("fill", "Fill") { SupportsPlanning = true };

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            foreach (var id in valueIds)
            {
                var index = int.Parse(id.AsSpan("v/".Length));
                RegisterField(new FieldMetadata(id, "Value", IntegerSpec.UInt8),
                    () => FieldValue.Of((int)_values[index]), value => _values[index] = (byte)value.AsInt32());
            }
            return base.ResolveFieldMetadata(valueIds);
        }

        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
            new()
            {
                Changes = _values.Select((value, index) =>
                    new PlannedChange($"v/{index}", "Value", FieldValue.Of((int)value), FieldValue.Of(1))).ToArray(),
            };

        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
            ActionScope scope, ActionPlan plan) => ApplyEdits(plan.Changes.Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray());

        public override IReadOnlyList<FileOutput> Serialize() => [new(path, _values.ToArray())];
        public DocumentMemento CaptureMemento() => CaptureSnapshot();
        protected override DocumentMemento CaptureSnapshot() => DocumentMemento.Snapshot(_values.ToArray(), _values.Length);
        protected override void LoadSnapshot(DocumentMemento snapshot) => _values = ((SnapshotMemento<byte[]>)snapshot).State.ToArray();
    }
}
