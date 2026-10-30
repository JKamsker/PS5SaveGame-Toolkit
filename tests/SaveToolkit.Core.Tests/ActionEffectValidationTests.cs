using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class ActionEffectValidationTests
{
    [Fact]
    public void BoundPlanningDocumentDoesNotAutomaticallyOptIntoEffects()
    {
        using var document = new LegacyPlanningDocument();
        var index = new ViewIndex(document);
        index.Add(document.BuildView());
        var plan = EffectPlan(new IntegerSpec(0, 10) { ReadOnly = true }, FieldValue.Of(1), FieldValue.Of(2));
        var error = Assert.Throws<InvalidOperationException>(() => FieldMetadataPlanning.Validate(index, document, plan));
        Assert.Contains("opted-in", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EffectAdmissionPreservesAbsentNullAndRejectsWriteOrCallbackAuthority()
    {
        using var document = new EffectDocumentForValidation();
        var index = new ViewIndex(document);
        index.Add(document.BuildView());
        var absent = EffectPlan(new IntegerSpec(0, 10) { ReadOnly = true }, FieldValue.Null, FieldValue.Of(1))
            with { Changes = [EffectPlan(new IntegerSpec(0, 10) { ReadOnly = true }, FieldValue.Null, FieldValue.Of(1)).Changes[0] with { OldExists = false }] };
        Assert.Empty(FieldMetadataPlanning.Validate(index, document, absent));
        var observationAbsent = absent with
        {
            Changes = [absent.Changes[0] with
            {
                Effect = absent.Changes[0].Effect! with { Kind = ActionEffectKind.Observation },
            }],
        };
        Assert.Throws<InvalidOperationException>(() => FieldMetadataPlanning.Validate(index, document, observationAbsent));

        var wrongType = EffectPlan(new IntegerSpec(0, 10) { ReadOnly = true }, FieldValue.Of("1"), FieldValue.Of(2));
        Assert.Throws<InvalidOperationException>(() => FieldMetadataPlanning.Validate(index, document, wrongType));

        var writable = EffectPlan(new IntegerSpec(0, 10), FieldValue.Of(1), FieldValue.Of(2));
        Assert.Throws<InvalidOperationException>(() => FieldMetadataPlanning.Validate(index, document, writable));

        var callback = EffectPlan(new ReferenceSpec("items")
        {
            ReadOnly = true,
            Assess = _ => new ReferenceEligibility { Available = true, Eligible = true },
        }, FieldValue.Of("unknown"), FieldValue.Of("new-unknown"));
        Assert.Throws<InvalidOperationException>(() => FieldMetadataPlanning.Validate(index, document, callback));

        var displayOnly = EffectPlan(new ReferenceSpec("items") { ReadOnly = true },
            FieldValue.Of("unknown"), FieldValue.Of("new-unknown"));
        var metadata = FieldMetadataPlanning.Validate(index, document, displayOnly);
        Assert.Empty(metadata);

        var nestedCallback = EffectPlan(new UnionSpec([
            new UnionOption("number", "Number", new IntegerSpec(0, 10)),
            new UnionOption("reference", "Reference", new ReferenceSpec("items")
            {
                ReadOnly = true,
                Assess = _ => new ReferenceEligibility { Available = true, Eligible = true },
            }),
        ]) { ReadOnly = true }, FieldValue.Union("number", FieldValue.Of(1)),
            FieldValue.Union("number", FieldValue.Of(2)));
        Assert.Throws<InvalidOperationException>(() => FieldMetadataPlanning.Validate(index, document, nestedCallback));

        var selectedOutOfRange = EffectPlan(new UnionSpec([
            new UnionOption("number", "Number", new IntegerSpec(0, 10)),
            new UnionOption("text", "Text", new TextSpec { ReadOnly = true }),
        ]) { ReadOnly = true }, FieldValue.Union("number", FieldValue.Of(1)),
            FieldValue.Union("number", FieldValue.Of(999)));
        Assert.Throws<InvalidOperationException>(() => FieldMetadataPlanning.Validate(index, document, selectedOutOfRange));

        var badConfidence = displayOnly with
        {
            Changes = [displayOnly.Changes[0] with
            {
                Effect = displayOnly.Changes[0].Effect! with
                {
                    Presentation = new FieldPresentation { Confidence = (Confidence)999 },
                },
            }],
        };
        Assert.Throws<InvalidOperationException>(() => ActionPlanSnapshots.Snapshot(badConfidence));
    }

    private static ActionPlan EffectPlan(FieldSpec spec, FieldValue oldValue, FieldValue newValue) => new()
    {
        Changes = [new PlannedChange("effect", "Effect", oldValue, newValue)
        {
            Effect = new(ActionEffectKind.StructuralMemento, spec),
        }],
    };

    private sealed class LegacyPlanningDocument : BoundSaveDocument, IActionPlanningDocument
    {
        public override string Title => "Legacy";
        public override ViewNode BuildView() => new SectionNode(Title,
            [new ActionNode("action", "Action") { SupportsPlanning = true }]);
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
            EffectPlan(new IntegerSpec(0, 10) { ReadOnly = true }, FieldValue.Of(1), FieldValue.Of(2));
        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) => new();
    }

    private sealed class EffectDocumentForValidation : BoundSaveDocument, IActionEffectDocument
    {
        public override string Title => "Effect validation";
        public override ViewNode BuildView() => new TextNode(Title);
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public DocumentMemento CaptureMemento() => DocumentMemento.Snapshot(0, 1);
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) => new();
        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) => new();
    }
}
