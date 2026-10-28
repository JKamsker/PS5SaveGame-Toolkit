using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed class LsmfLiveActions(LsmfLiveWorkspace workspace, Func<LsmfLiveSource, LsmfNumber, SaveToolkit.Sdk.Views.FieldNode> bind,
    Func<IReadOnlyList<ValueEdit>, EditOutcome> edit)
{
    internal const string Restore = "bg3.live.restore-quantities";
    internal const string Swap = "bg3.live.swap-map-keys";
    private readonly Guid _identity = Guid.NewGuid();

    internal ActionPlan Plan(string id, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (id is not (Restore or Swap)) return Error("Unknown NewAge action.");
        if (parameters.Count != 0) return Error("This action has no parameters.");
        if (scope.QueryScope is { } query && (scope.RowKeys is null ||
            !workspace.Collections.Any(c => c.Id == query.CollectionId &&
                c.Id.EndsWith(id == Swap ? "/slots" : "/values", StringComparison.Ordinal))))
            return Error("Use a Core-resolved query scope from the matching NewAge collection.");
        if (scope.RowKeys is { } keys && keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
            return Error("Duplicate selected rows.");
        return id == Swap ? PlanSwap(scope) : PlanRestore(scope);
    }

    internal EditOutcome Apply(string id, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        var plan = Plan(id, parameters, scope);
        if (plan.Errors.Count != 0) return new EditOutcome { Errors = plan.Errors, Warnings = plan.Warnings };
        if (id == Restore)
            return edit(plan.Changes.Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray())
                with { Warnings = plan.Warnings };
        if (plan.Changes.Count == 0) return new EditOutcome { Warnings = plan.Warnings };
        var slots = SelectedSlots(scope);
        var snapshot = Snapshot();
        try { slots[0].Source.Swap(slots[0].Slot, slots[1].Slot); }
        catch (SaveFormatException exception)
        {
            workspace.Restore(snapshot.State);
            return EditOutcome.Rejected(id, exception.Message);
        }
        return new EditOutcome
        {
            Changes = plan.Changes.Select(c => new ValueChange(c.ValueId, c.OldValue, c.NewValue)).ToArray(),
            Revert = snapshot, StructureChanged = true, Warnings = plan.Warnings,
        };
    }

    internal DocumentMemento RestoreMemento(DocumentMemento memento)
    {
        if (memento is not LiveMemento snapshot || snapshot.Identity != _identity)
            throw new ArgumentException("This undo token belongs to another NewAge document.", nameof(memento));
        var inverse = Snapshot();
        workspace.Restore(snapshot.State);
        return inverse;
    }

    private ActionPlan PlanRestore(ActionScope scope)
    {
        var members = workspace.Sources.SelectMany(source => source.Numbers.Select(number => (Source: source, Number: number)))
            .ToDictionary(pair => pair.Source.Id(pair.Number), StringComparer.Ordinal);
        var keys = scope.RowKeys ?? members.Where(pair => pair.Value.Number.Family == "Stacks").Select(pair => pair.Key).ToArray();
        if (keys.Any(key => !members.ContainsKey(key))) return Error("Selection contains an unknown NewAge value row.");
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        foreach (var key in keys)
        {
            var (source, number) = members[key];
            if (number.Family != "Stacks") { skipped.Add(new(key, "Only decoded positive stack members are eligible.")); continue; }
            _ = bind(source, number);
            var current = source.Get(number);
            if (current == number.Original) { skipped.Add(new(key, "Already at imported quantity.")); continue; }
            changes.Add(new(key, $"Group/member {number.Row}: stored quantity", FieldValue.Of(current), FieldValue.Of(number.Original)));
        }
        return new ActionPlan { Changes = changes, SkippedRows = skipped, Warnings = [LsmfLiveSource.Warning] };
    }

    private ActionPlan PlanSwap(ActionScope scope)
    {
        if (scope.RowKeys is not { Count: 2 }) return Error("Select exactly two entries in one container; document scope is unavailable.");
        var slots = SelectedSlots(scope);
        if (slots.Length != 2 || slots[0].Source != slots[1].Source || slots[0].Slot.ContainerRow != slots[1].Slot.ContainerRow)
            return Error("Select two known entries from the same NewAge source and container.");
        var first = slots[0];
        var second = slots[1];
        return new ActionPlan
        {
            Changes =
            [
                new(first.Source.Prefix + "/" + first.Slot.Key + "/key", "First container map key candidate",
                    FieldValue.Of(first.Source.Slot(first.Slot)), FieldValue.Of(second.Source.Slot(second.Slot))),
                new(second.Source.Prefix + "/" + second.Slot.Key + "/key", "Second container map key candidate",
                    FieldValue.Of(second.Source.Slot(second.Slot)), FieldValue.Of(first.Source.Slot(first.Slot))),
            ],
            Warnings = [LsmfLiveSource.Warning, "Swaps only two existing u16 map keys. Dense ordinals, pointers, item membership, quantities and equipment are preserved; visible inventory ordering is unconfirmed."],
        };
    }

    private (LsmfLiveSource Source, LsmfSlot Slot)[] SelectedSlots(ActionScope scope) =>
        (scope.RowKeys ?? []).SelectMany(key => workspace.Sources.SelectMany(source => source.Slots
            .Where(slot => source.Prefix + "/" + slot.Key == key).Select(slot => (source, slot)))).ToArray();

    private LiveMemento Snapshot() => new(_identity, workspace.Capture());
    private static ActionPlan Error(string message) => new() { Errors = [new FieldError("action", message)], Warnings = [LsmfLiveSource.Warning] };

    // Scoped inverse token: NewAge is the only state this action changes. Other resource/summary
    // edits remain owned by the ordinary scalar history; replay uses stable live source objects.
    private sealed class LiveMemento(Guid identity, IReadOnlyList<IReadOnlyDictionary<int, uint>> state) : DocumentMemento
    {
        internal Guid Identity { get; } = identity;
        internal IReadOnlyList<IReadOnlyDictionary<int, uint>> State { get; } = state;
        public override long SizeBytes => 64 + State.Sum(patches => 64L + patches.Count * 24L);
    }
}
