using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Restocks existing ordinary consumables only, never currency, equipment or progression objects.</summary>
internal sealed class InventoryPlanning(Ff7Schema schema, GameplayFields gameplay, ReferenceData references)
{
    public const string ActionId = "inventory/restock-existing";
    private const uint MaximumTarget = 99; // Pinned upstream ordinary-stack editor policy, not a verified game cap.
    private readonly IReadOnlyDictionary<uint, string> _consumables = EmbeddedCatalogs.Records(schema, "ff7r-items")
        .Where(record => record.Category == "Items" && !record.Name.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(record => uint.Parse(record.Key, CultureInfo.InvariantCulture), record => record.Name);

    public ActionNode View() => new(ActionId, "Restock existing consumables")
    {
        SupportsPlanning = true,
        Description = "Preview existing positive ordinary consumable stacks below the target. Currency, keys, tokens, materials, cards, gear, unknown and zero/sentinel rows are skipped. " +
            "Target 99 is upstream editor policy, not a proven PS5 cap. " + WriteWarnings.Experimental + " " + EmbeddedCatalogs.Attribution,
        Parameters = [new("quantity", "Target count", IntegerSpec.UInt32 with { Min = 1, Max = MaximumTarget }, FieldValue.Of((long)MaximumTarget))],
    };

    public ActionPlan Plan(IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (scope.QueryScope is { } query && (query.CollectionId != "inventory" || scope.RowKeys is null))
            return Error("scope", "Use a Core-resolved inventory query scope.");
        uint target;
        try
        {
            if (parameters.Count != 1 || !parameters.TryGetValue("quantity", out var value) || (target = value.AsUInt32()) is < 1 or > MaximumTarget)
                return Error("quantity", "Target count must be between 1 and 99.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            return Error("quantity", "Target count must be an integer between 1 and 99.");
        }

        var keys = scope.RowKeys ?? Enumerable.Range(0, GameplayFields.InventoryCount).Select(slot => $"inventory/{slot}").ToArray();
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var parts = key.Split('/');
            if (parts.Length != 2 || parts[0] != "inventory" || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var slot) ||
                slot < 0 || slot >= GameplayFields.InventoryCount || key != $"inventory/{slot}")
            {
                skipped.Add(new(key, "Not a current inventory slot."));
                continue;
            }

            var type = gameplay.TypeId(false, slot);
            var quantity = gameplay.Quantity(slot);
            var range = gameplay.QuantityRange(slot);
            var reason = !_consumables.ContainsKey(type) ? "Not a mapped ordinary consumable."
                : quantity <= 0 ? "Zero, negative or sentinel stack; no acquisition is inferred."
                : range is null || target > range.Max ? "Target exceeds this row's editor range."
                : quantity >= target ? "Already at or above target; nothing is reduced." : null;
            if (reason is not null) skipped.Add(new(key, reason));
            else
            {
                changes.Add(new($"{key}/quantity", $"{references.Item(type)} · slot {slot}", FieldValue.Of((long)quantity), FieldValue.Of((long)target)));
                sources.Add(references.Source("ff7r-items", type));
            }
        }

        return new() { Changes = changes, SkippedRows = skipped, Warnings = [WriteWarnings.Experimental +
            "Only existing positive ordinary consumable counts change. Target 99 is an upstream editor policy; acquisition flags are preserved.",
            .. sources.Order(StringComparer.Ordinal)] };
    }

    private static ActionPlan Error(string field, string message) => new() { Errors = [new(field, message)] };
}
