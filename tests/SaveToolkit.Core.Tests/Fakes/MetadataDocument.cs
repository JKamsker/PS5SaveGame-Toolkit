using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

internal sealed class MetadataDocument : BoundSaveDocument, IFieldMetadataDocument, IActionPlanningDocument
{
    private int _visible = 1;
    private QuantityState _model = new(2);
    public int Quantity => _model.Quantity;
    public int Views { get; private set; }
    public int Expansions { get; private set; }
    public int Writes { get; private set; }
    public string? PlanFailure { get; set; }
    public int Maximum { get; set; } = 10;
    public string CanonicalLabel { get; set; } = "Quantity";
    public override string Title => "Metadata";

    public MetadataDocument() => Bind();

    private void Bind()
    {
        var model = _model; // Deliberately capture this revision's model, not the document property.
        RegisterField(new FieldMetadata("quantity", CanonicalLabel, new IntegerSpec(0, Maximum)),
            () => FieldValue.Of(model.Quantity), value => { model.Quantity = value.AsInt32(); Writes++; },
            value => value.AsInt32() == 6 ? "Custom validation rejected six." : null);
        RegisterField(new FieldMetadata("total", "Total", IntegerSpec.Int32 with { ReadOnly = true })
            { BindingKind = FieldBindingKind.Derived }, () => FieldValue.Of(model.Quantity * 2));
    }

    public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
    {
        Bind();
        return base.ResolveFieldMetadata(valueIds);
    }

    public override ViewNode BuildView()
    {
        Views++;
        return new SectionNode(Title,
        [
            new SectionNode("Unloaded", []) { LazyId = "items" },
            Field("visible", "Legacy visible field", IntegerSpec.Int32, () => FieldValue.Of(_visible), value => _visible = value.AsInt32()),
            new ActionNode("fill", "Fill") { SupportsPlanning = true },
            new ActionNode("replace", "Replace model"),
        ]);
    }

    public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
        Structural(() => _model = new QuantityState(4));

    protected override DocumentMemento CaptureSnapshot() => DocumentMemento.Snapshot(Quantity, sizeof(int));
    protected override void LoadSnapshot(DocumentMemento snapshot) => _model = new QuantityState(((SnapshotMemento<int>)snapshot).State);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        Expansions++;
        return [Field("quantity", "Quantity")];
    }

    protected override IEnumerable<ValueChange> DerivedChanges(IReadOnlyList<ValueChange> applied) =>
        applied.Where(change => change.ValueId == "quantity").Select(change => new ValueChange("total", FieldValue.Of(change.OldValue.AsInt32() * 2),
            FieldValue.Of(change.NewValue.AsInt32() * 2)));

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) => new()
    {
        Changes =
        [
            new PlannedChange(PlanFailure == "missing" ? "missing" : "quantity", "Quantity",
                FieldValue.Of(Quantity), FieldValue.Of(PlanFailure == "range" ? 11 : 7)),
            new PlannedChange("total", "Total", FieldValue.Of(Quantity * 2), FieldValue.Of(14))
                { Derived = PlanFailure != "derived" },
        ],
    };

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
        ActionScope scope, ActionPlan plan) => ApplyEdits(plan.Changes.Where(change => !change.Derived)
            .Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray());

    public override IReadOnlyList<FileOutput> Serialize() => [new FileOutput("save.dat", new byte[] { (byte)Quantity })];

    private sealed class QuantityState(int quantity)
    {
        public int Quantity { get; set; } = quantity;
    }
}
