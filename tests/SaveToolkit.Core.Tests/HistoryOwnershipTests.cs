using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class HistoryOwnershipTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ReusingProviderChangeListCannotRewriteEarlierUndoEntries(int mode)
    {
        var document = new ReusingDocument();
        using var fixture = new MutationSessionFixture(document, openOriginal: () => new ReusingDocument());
        var session = fixture.Session;
        _ = session.BuildView();
        Assert.True(Apply(1).Ok);
        Assert.True(Apply(2).Ok);

        Assert.True(session.Undo().Ok);
        Assert.Equal(1, document.Value);
        Assert.True(session.Undo().Ok);
        Assert.Equal(0, document.Value);
        Assert.False(session.State.IsDirty);
        Assert.True(session.Redo().Ok);
        Assert.Equal(1, document.Value);
        Assert.True(session.Redo().Ok);
        Assert.Equal(2, document.Value);

        SessionEditResult Apply(int value)
        {
            var json = JsonSerializer.SerializeToElement(value);
            if (mode == 0) return session.ApplyEdits([new("value", json)]);
            var parameters = new Dictionary<string, JsonElement> { ["target"] = json };
            if (mode == 1) return session.InvokeAction("set", parameters);
            var preview = session.PlanAction("planned-set", parameters);
            Assert.NotNull(preview.PlanId);
            return session.ApplyActionPlan(preview.PlanId, preview.Revision);
        }
    }

    private sealed class ReusingDocument : BoundSaveDocument, IActionPlanningDocument
    {
        private readonly List<ValueChange> _changes = [];
        public int Value { get; private set; }
        public override string Title => "Reused outcomes";
        public override ViewNode BuildView() => new SectionNode(Title,
        [
            Field("value", "Value", IntegerSpec.UInt8, () => FieldValue.Of(Value), value => Value = value.AsInt32()),
            Action("set", false), Action("planned-set", true),
        ]);
        private static ActionNode Action(string id, bool planning) => new(id, id)
        {
            SupportsPlanning = planning,
            Parameters = [new FieldNode("target", "Target", IntegerSpec.UInt8, FieldValue.Of(0))],
        };
        public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
        {
            var outcome = base.ApplyEdits(edits);
            _changes.Clear();
            _changes.AddRange(outcome.Changes);
            return outcome with { Changes = _changes };
        }
        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
            new() { Changes = ApplyEdits([new("value", parameters["target"])]).Changes };
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
            new() { Changes = [new("value", "Value", FieldValue.Of(Value), parameters["target"])] };
        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
            ActionScope scope, ActionPlan plan) => ApplyEdits([new("value", parameters["target"])]);
        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)Value })];
    }
}
