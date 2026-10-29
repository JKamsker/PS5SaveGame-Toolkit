using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Routes pure planners and registers hidden scalar bindings before their atomic apply.</summary>
internal sealed class DocumentPlanning(Ff7Schema schema, GameplayFields gameplay, ReferenceData references, CharacterView characters)
{
    private readonly MateriaPlanning _materia = new(schema, gameplay, references);
    private readonly InventoryPlanning _inventory = new(schema, gameplay, references);
    private readonly CharacterPlanning _characters = new(schema, characters, references);

    public ActionNode InventoryView() => _inventory.View();
    public IReadOnlyList<ViewNode> MateriaViews() => [_materia.View(), _materia.View(stars: true)];
    public IReadOnlyList<ActionNode> CharacterViews() => _characters.Views();

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
        CharacterPlanning.Handles(actionId) ? _characters.Plan(actionId, parameters, scope) :
            actionId == InventoryPlanning.ActionId ? _inventory.Plan(parameters, scope) : _materia.Plan(actionId, parameters, scope);

    public EditOutcome Apply(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan,
        Func<ScalarField, FieldNode> bind, Func<IReadOnlyList<ValueEdit>, EditOutcome> apply)
        => ActionPlans.ApplyScalar(plan, () => Plan(actionId, parameters, scope),
            new("action", "The plan is invalid or has changed; preview again."),
            current => PrepareBindings(actionId, current, bind),
            current => [.. current.Changes.Select(change => new ValueEdit(change.ValueId, change.NewValue))],
            apply, ActionPlanWarningPolicy.UsePlan);

    private void PrepareBindings(string actionId, ActionPlan plan, Func<ScalarField, FieldNode> bind)
    {
        if (CharacterPlanning.Handles(actionId))
        {
            _ = characters.Build();
            return;
        }
        foreach (var change in plan.Changes)
        {
            var parts = change.ValueId.Split('/');
            _ = gameplay.Row(parts[0] == "materia", int.Parse(parts[1], CultureInfo.InvariantCulture), bind);
        }
    }
}
