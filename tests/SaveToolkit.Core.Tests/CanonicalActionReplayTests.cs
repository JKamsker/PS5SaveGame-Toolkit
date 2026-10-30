using System.Text.Json;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class CanonicalActionReplayTests
{
    [Theory]
    [InlineData(5001, false)]
    [InlineData(2501, true)]
    public void AdmittedBulkActionCanUndoAndRedoEveryByte(int count, bool aliases)
    {
        var document = new BulkDocument(count, aliases);
        using var fixture = new MutationSessionFixture(document,
            openOriginal: () => new BulkDocument(count, aliases));
        var session = fixture.Session;
        _ = session.BuildView();
        var original = document.Serialize().Single().Content.ToArray();
        var preview = session.PlanAction("fill", new Dictionary<string, JsonElement>());
        Assert.NotNull(preview.PlanId);
        var applied = session.ApplyActionPlan(preview.PlanId, preview.Revision);
        Assert.True(applied.Ok, string.Join(" | ", applied.Errors.Select(error => error.Message)));
        var changed = session.Serialize().Single().Content.ToArray();
        Assert.All(changed, value => Assert.Equal(1, value));

        var undo = session.Undo();

        Assert.True(undo.Ok, string.Join(" | ", undo.Errors.Select(error => error.Message)));
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.False(session.State.IsDirty);
        Assert.Empty(session.Serialize());
        Assert.True(session.Redo().Ok);
        Assert.Equal(changed, session.Serialize().Single().Content.ToArray());
    }

    private sealed class BulkDocument : BoundSaveDocument, IFieldMetadataDocument, IActionPlanningDocument
    {
        private readonly byte[] _values;
        private readonly bool _aliases;

        public BulkDocument(int count, bool aliases)
        {
            _values = new byte[count];
            _aliases = aliases;
            for (var index = 0; index < count; index++)
            {
                var position = index;
                var id = $"value/{index}";
                RegisterField(new FieldMetadata(id, id, IntegerSpec.UInt8),
                    () => FieldValue.Of(_values[position]), value => _values[position] = (byte)value.AsInt32());
                if (aliases)
                    RegisterField(new FieldMetadata($"alias/{index}", id, IntegerSpec.UInt8 with { ReadOnly = true })
                    {
                        BindingKind = FieldBindingKind.Derived,
                        CanonicalValueId = id,
                    }, () => FieldValue.Of(_values[position]));
            }
        }

        public override string Title => "Bulk values";
        public override ViewNode BuildView() => new SectionNode(Title,
            [new ActionNode("fill", "Fill") { SupportsPlanning = true }]);
        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", _values.ToArray())];
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
        {
            var changes = new List<PlannedChange>();
            for (var index = 0; index < _values.Length; index++)
            {
                changes.Add(new($"value/{index}", "Value", FieldValue.Of(_values[index]), FieldValue.Of(1)));
                if (_aliases)
                    changes.Add(new($"alias/{index}", "Alias", FieldValue.Of(_values[index]), FieldValue.Of(1)) { Derived = true });
            }
            return new() { Changes = changes };
        }

        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
            ActionScope scope, ActionPlan plan)
        {
            var result = ApplyEdits(plan.Changes.Where(change => !change.Derived)
                .Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray());
            return result with
            {
                Changes = plan.Changes.Select(change => new ValueChange(change.ValueId, change.OldValue, change.NewValue)).ToArray(),
            };
        }
    }
}
