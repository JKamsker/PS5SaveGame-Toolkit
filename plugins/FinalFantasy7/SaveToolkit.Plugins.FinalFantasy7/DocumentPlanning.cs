using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Routes pure planners and registers hidden scalar bindings before their atomic apply.</summary>
internal sealed class DocumentPlanning(Ff7Schema schema, GameplayFields gameplay, ReferenceData references)
{
    private readonly MateriaPlanning _materia = new(schema, gameplay, references);
    private readonly InventoryPlanning _inventory = new(schema, gameplay, references);

    public ActionNode InventoryView() => _inventory.View();
    public IReadOnlyList<ViewNode> MateriaViews() => [_materia.View(), _materia.View(stars: true)];

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
        actionId == InventoryPlanning.ActionId ? _inventory.Plan(parameters, scope) : _materia.Plan(actionId, parameters, scope);

    public EditOutcome Apply(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan,
        Func<ScalarField, FieldNode> bind, Func<IReadOnlyList<ValueEdit>, EditOutcome> apply)
    {
        var current = Plan(actionId, parameters, scope);
        if (current.Errors.Count > 0 || plan.Errors.Count > 0 || !current.Changes.SequenceEqual(plan.Changes) ||
            !current.SkippedRows.SequenceEqual(plan.SkippedRows) || !current.Warnings.SequenceEqual(plan.Warnings))
            return EditOutcome.Rejected("action", "The plan is invalid or has changed; preview again.");
        foreach (var change in current.Changes)
        {
            var parts = change.ValueId.Split('/');
            _ = gameplay.Row(parts[0] == "materia", int.Parse(parts[1], CultureInfo.InvariantCulture), bind);
        }

        return apply([.. current.Changes.Select(change => new ValueEdit(change.ValueId, change.NewValue))]);
    }
}
