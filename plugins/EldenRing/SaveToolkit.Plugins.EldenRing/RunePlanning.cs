using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing;

// F6 preview for the already-supported fixed-size edit. This never expands the history guard.
internal sealed class RunePlanning(byte[] bytes, ParsedSave save)
{
    private const string Parameter = "runes";
    public const string Warning = "Experimental; no console reload validation. Only held runes change. "
        + "Lifetime history and its profile copy, equipment, vitals and regulation remain unchanged.";

    public CharacterSlot Slot(string actionId) => save.Slots.FirstOrDefault(slot => slot.Active && ActionId(slot) == actionId)
        ?? throw new KeyNotFoundException($"Unknown planned action '{actionId}'.");

    public ActionNode Action(CharacterSlot slot) => new(ActionId(slot), "Review held-rune change")
    {
        SupportsPlanning = true,
        Description = Warning,
        Parameters = [new FieldNode(Parameter, "Runes held", new IntegerSpec(0, Maximum(slot)), Current(slot))],
    };

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        var slot = Slot(actionId);
        var errors = new List<FieldError>();
        if (!save.CanEdit) errors.Add(new(Parameter, "This profile/character version is read-only."));
        if (scope.RowKeys is not null || scope.QueryScope is not null)
            errors.Add(new(Parameter, "This single-character action requires document scope; row selections are not supported."));
        if (parameters.Count != 1 || !parameters.TryGetValue(Parameter, out var value)
            || value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > Maximum(slot))
            errors.Add(new(Parameter, $"Choose a whole rune balance from 0 to {Maximum(slot)} within existing lifetime history."));
        if (errors.Count > 0) return new ActionPlan { Errors = errors, Warnings = [Warning] };
        var proposed = parameters[Parameter];
        var old = Current(slot);
        return new ActionPlan
        {
            Changes = old == proposed ? [] : [new PlannedChange(ValueId(slot), $"Slot {slot.Index} · Runes held", old, proposed)],
            Warnings = [Warning],
        };
    }

    public static string ActionId(CharacterSlot slot) => $"slots/{slot.Index}/runes/set";
    private static string ValueId(CharacterSlot slot) => $"slots/{slot.Index}/runes";
    private FieldValue Current(CharacterSlot slot) => FieldValue.Of((long)EldenRingParser.ReadUInt32(bytes,
        slot.PlayerOffset!.Value + EldenRingLayout.RunesOffset));
    private uint Maximum(CharacterSlot slot) => Math.Min(EldenRingLayout.MaximumRunes,
        EldenRingParser.ReadUInt32(bytes, slot.PlayerOffset!.Value + EldenRingLayout.RuneMemoryOffset));
}
