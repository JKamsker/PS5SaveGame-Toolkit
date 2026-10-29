using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing;

internal sealed class WritePlanning(byte[] bytes, ParsedSave save, IReadOnlyList<SemanticCharacter> characters,
    ValueWrites writes, ReferenceBrowser catalog)
{
    public static string RefillId(CharacterSlot slot) => $"slots/{slot.Index}/inventory/refill";
    public static string LinkedId(CharacterSlot slot) => $"slots/{slot.Index}/runes/linked";
    public bool Owns(string id) => save.Slots.Any(slot => slot.Active && (RefillId(slot) == id || LinkedId(slot) == id));
    public CharacterSlot Slot(string id) => save.Slots.Single(slot => slot.Active && (RefillId(slot) == id || LinkedId(slot) == id));

    public IReadOnlyList<ActionNode> Actions(CharacterSlot slot) =>
    [
        RefillAction(slot.Index),
        new(LinkedId(slot), "Review linked rune/history increase")
        {
            SupportsPlanning = true,
            Description = ValueWrites.Warning + " Raises both history mirrors only when needed, never lowers or repeatedly adds history. "
                + "Ceiling is the larger of imported history and observed 4427, bounded by the held-rune limit.",
            Parameters = [new FieldNode("runes", "Runes held", new IntegerSpec(0, writes.RuneCeiling(slot)),
                Number(slot.PlayerOffset!.Value + EldenRingLayout.RunesOffset))],
        },
    ];

    public static ActionNode RefillAction(int slot) => new($"slots/{slot}/inventory/refill", "Refill supported ordinary stacks")
    {
        SupportsPlanning = true,
        Description = ValueWrites.QuantityWarning + " Preview lists excluded rows; no records are added or removed.",
    };

    public ActionPlan Plan(string id, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        var slot = Slot(id);
        if (!save.CanEdit) return Error(id, "This character/profile version is read-only.");
        return id == RefillId(slot) ? Refill(slot, parameters, scope) : Linked(slot, parameters, scope);
    }

    private ActionPlan Linked(CharacterSlot slot, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (scope.RowKeys is not null || scope.QueryScope is not null)
            return Error(LinkedId(slot), "Linked rune/history changes require document scope.");
        if (parameters.Count != 1 || !parameters.TryGetValue("runes", out var proposed)
            || ValueWrites.IntegerError(proposed, 0, writes.RuneCeiling(slot)) is not null)
            return Error("runes", $"Choose a whole rune balance from 0 to {writes.RuneCeiling(slot)}.");
        var player = slot.PlayerOffset!.Value;
        var oldHistory = Number(player + EldenRingLayout.RuneMemoryOffset);
        var newHistory = FieldValue.Of((long)Math.Max(oldHistory.AsUInt32(), proposed.AsUInt32()));
        var changes = new List<PlannedChange>();
        if (oldHistory != newHistory)
            changes.Add(new($"slots/{slot.Index}/rune-memory", "Lifetime history · live and menu copies", oldHistory, newHistory) { Derived = true });
        var oldRunes = Number(player + EldenRingLayout.RunesOffset);
        if (oldRunes != proposed) changes.Add(new($"slots/{slot.Index}/runes", "Runes held", oldRunes, proposed));
        return new ActionPlan { Changes = changes, Warnings = [ValueWrites.Warning,
            "Lifetime-history meaning is Inferred; the game may reject this change. Both known history copies are updated, without spending/death/recovery simulation."] };
    }

    private ActionPlan Refill(CharacterSlot slot, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (parameters.Count != 0) return Error(RefillId(slot), "Refill takes no parameters.");
        var character = characters.SingleOrDefault(character => character.Slot == slot.Index);
        if (character is null) return Error(RefillId(slot), "No supported inventory layout.");
        var rows = character.Containers.SelectMany(container => container.Entries).ToArray();
        if (scope.QueryScope is { } query && (scope.RowKeys is null
            || !character.Containers.Any(container => InventoryBrowser.Id(slot.Index, container.Id) == query.CollectionId)))
            return Error(RefillId(slot), "Use a server-resolved held/storage query from this character.");
        var selected = scope.RowKeys?.ToHashSet(StringComparer.Ordinal);
        var knownRows = rows.Select(row => row.Key(slot.Index)).ToHashSet(StringComparer.Ordinal);
        if (selected is not null && (selected.Count != scope.RowKeys!.Count
            || !selected.IsSubsetOf(knownRows)))
            return Error(RefillId(slot), "Selection contains duplicate, unknown or other-character rows.");
        if (scope.QueryScope is { } scoped && rows.Any(row => selected!.Contains(row.Key(slot.Index))
            && InventoryBrowser.Id(slot.Index, row.Container) != scoped.CollectionId))
            return Error(RefillId(slot), "Selection contains rows outside the requested collection.");

        var eligible = writes.Quantities.ToDictionary(row => row.Key, StringComparer.Ordinal);
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        var attribution = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows.Where(row => selected is null ? !row.IsEmpty : selected.Contains(row.Key(slot.Index))))
        {
            var key = row.Key(slot.Index);
            if (!eligible.TryGetValue(key, out var supported))
            {
                skipped.Add(new(key, "Protected: outside ordinary-stack allowlist or ambiguous/zero/count-inconsistent row."));
                continue;
            }
            var old = FieldValue.Of((long)writes.Current(row));
            var next = FieldValue.Of((long)supported.Ceiling);
            if (old == next) { skipped.Add(new(key, "Already at the conservative observed ceiling.")); continue; }
            var reference = catalog.Find(row.RawId);
            var label = reference?.Name ?? row.RawId;
            if (reference is not null) attribution.Add(reference.AttributionText);
            changes.Add(new(ValueWrites.QuantityId(row, slot.Index), $"{row.Container} · {label} · Quantity", old, next));
        }
        return new ActionPlan { Changes = changes, SkippedRows = skipped,
            Warnings = [ValueWrites.QuantityWarning, .. attribution.Order(StringComparer.Ordinal)] };
    }

    private FieldValue Number(int offset) => FieldValue.Of((long)EldenRingParser.ReadUInt32(bytes, offset));
    private static ActionPlan Error(string id, string message) => new() { Errors = [new(id, message)], Warnings = [ValueWrites.Warning] };
}
