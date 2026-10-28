using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

/// <summary>A calculated-balance target using only the existing fixed-width career counter.</summary>
internal sealed class RiftBoltPlanning(RiftScalarWrites writes)
{
    internal const string ActionId = "bolts/set-target";
    internal const string TargetParameter = "target";
    private const string Policy = "Calculated balance is CareerBolts minus SpentBolts, not a HUD-calibrated wallet. " +
        "Only CareerBolts changes; spending history stays unchanged. Bounds retain serialized capacity and the observed corpus ceiling, not a proven game cap.";

    internal ActionNode? Build()
    {
        if (!writes.HasBalance) return null;
        return new(ActionId, "Set calculated bolt balance")
        {
            SupportsPlanning = true,
            Description = Policy + " " + RiftScalarWrites.Warning,
            // The parameter spec is stable while spending changes; the plan enforces the remaining headroom.
            Parameters = [new(TargetParameter, "Target calculated balance", new IntegerSpec(0, CareerMaximum),
                FieldValue.Of(Int128.Clamp(writes.Balance.AsInteger(), 0, MaximumTarget)))
            {
                Confidence = Confidence.Inferred,
                Description = Policy,
                Warning = RiftScalarWrites.Warning,
            }],
        };
    }

    private Int128 CareerMaximum => writes.TryWrite(RiftScalarWrites.CareerId, out var career) ? ((IntegerSpec)career.Spec).Max : 0;

    private Int128 MaximumTarget => writes.TryWrite(RiftScalarWrites.CareerId, out var career) &&
        writes.TryWrite(RiftScalarWrites.SpentId, out var spent)
        ? Int128.Max(0, ((IntegerSpec)career.Spec).Max - spent.Value.AsInteger()) : 0;

    internal ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        ActionPlan Reject(string message) => new()
        {
            Errors = [new(actionId, message)], Warnings = [RiftScalarWrites.Warning, Policy],
        };
        if (actionId != ActionId) return Reject("Unknown planned action.");
        if (!writes.HasBalance) return Reject("This document has no supported writable bolt counters.");
        if (scope.RowKeys is not null || scope.QueryScope is not null)
            return Reject("The bolt target is document-wide; row and query selections are unsupported.");
        if (parameters.Count != 1 || !parameters.TryGetValue(TargetParameter, out var target) ||
            target.Kind != FieldValueKind.Integer || target.AsInteger() < 0 || target.AsInteger() > MaximumTarget)
            return Reject($"Target must be an integer between 0 and {MaximumTarget}; conservative width/corpus bounds apply.");
        _ = writes.TryWrite(RiftScalarWrites.CareerId, out var career);
        _ = writes.TryWrite(RiftScalarWrites.SpentId, out var spent);
        var next = FieldValue.Of(spent.Value.AsInteger() + target.AsInteger());
        if (career.Validate(next) is { } error) return Reject(error);
        if (next == career.Value)
            return new() { SkippedRows = [new(RiftScalarWrites.CareerId, "Already at the requested calculated balance.")], Warnings = [RiftScalarWrites.Warning, Policy] };
        return new()
        {
            Changes =
            [
                new(RiftScalarWrites.CareerId, "Career bolts", career.Value, next),
                new(RiftScalarWrites.BalanceId, "Calculated available bolts", writes.Balance, target) { Derived = true },
            ],
            Warnings = [RiftScalarWrites.Warning, Policy],
        };
    }
}
