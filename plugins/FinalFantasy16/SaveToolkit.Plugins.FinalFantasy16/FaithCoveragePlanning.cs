using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

// Workflow policy over existing fixed-width quantities; no allocation or gameplay-cap claims.
internal sealed class FaithCoveragePlanning
{
    public const string RestoreId = "restore-imported-main-stacks";
    private const string RestoreWarning = "Restore imported uses this workspace's immutable opening quantities, only when lower and positive. " +
        "Values already above that baseline stay unchanged. Zero allocated stacks are not vacancies or proven ownership. " +
        "Ordinary-item eligibility and names are Inferred; active-bank, pouch capacity and crafting dependencies remain unverified.";
    private static readonly Resource[] Resources = [new("set-gil", 1, "Gil", "Gil"),
        new("set-ap", 3, "AP", "Ability Points"), new("set-renown", 2, "Renown", "Renown")];
    private readonly Dictionary<int, OpeningStack> _opening;
    private readonly Dictionary<int, int> _openingCounts;
    private readonly bool _isSystem;

    public FaithCoveragePlanning(byte[] bytes, FaithInventory inventory, bool isSystem)
    {
        _isSystem = isSystem;
        _opening = inventory.Items.ToDictionary(item => item.Slot,
            item => new OpeningStack(item.Id, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(item.Offset + sizeof(int)))));
        _openingCounts = inventory.Items.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.Count());
    }

    public static ActionNode RestoreAction() => new(RestoreId, "Restore imported ordinary main-bank stacks")
    {
        SupportsPlanning = true,
        Description = FaithWriteMap.Warning + RestoreWarning + " Only potions/tonics (5, 6, 8–10) and common crafting materials (101–109) qualify; " +
            "currencies, special/unique/key/progression/upgrade items, unknown records and secondary bank are excluded.",
    };

    public static IReadOnlyList<ActionNode> ResourceActions(FaithCatalog catalog) => Resources.Select(resource =>
        new ActionNode(resource.ActionId, $"Set {resource.Label} balance")
        {
            SupportsPlanning = true,
            Description = FaithWriteMap.Warning + "Main-record storage Verified; resource name/ID join Inferred. " +
                $"Explicit target 0..{FaithQuantityPlanning.MaximumTarget}, the conservative observed F6 ceiling, not a legal cap. " +
                FaithStackPolicy.Warning(resource.ItemId, "resource") + " " + catalog.Provenance(resource.ItemId),
            Parameters = [new FieldNode(FaithQuantityPlanning.QuantityParameter, $"Target {resource.Label} balance",
                new IntegerSpec(0, FaithQuantityPlanning.MaximumTarget), FieldValue.Of(0)) { Confidence = Confidence.Inferred }],
        }).ToArray();

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope,
        byte[] bytes, FaithInventory inventory, FaithCatalog catalog)
    {
        if (_isSystem || !inventory.IsSupported) return Error(actionId, "Main inventory schema/allocation is unsupported, ambiguous or read-only.");
        var quantities = new FaithQuantityPlanning(bytes, inventory, catalog);
        if (actionId == RestoreId)
        {
            if (parameters.Count != 0) return Error(actionId, "Restore imported takes no parameters.");
            var counts = inventory.Items.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.Count());
            var plan = quantities.PlanTargets(actionId, scope, item => _opening[item.Slot].Quantity, Skip);
            return plan with { Warnings = [.. plan.Warnings, RestoreWarning] };

            string? Skip(FaithItem item, int current)
            {
                if (!_opening.TryGetValue(item.Slot, out var opening) || opening.ItemId != item.Id)
                    return "The slot's identity differs from import; unchanged.";
                if (_openingCounts.GetValueOrDefault(item.Id) != 1 || counts.GetValueOrDefault(item.Id) != 1)
                    return "Duplicate item identity is ambiguous; unchanged.";
                if (!IsOrdinary(item.Id) || !QualifiedOrdinary(catalog, item.Id))
                    return "Not a qualified named ordinary potion/tonic or common material; unchanged.";
                if (opening.Quantity <= 0 || current <= 0)
                    return "Opening and current stacks must both be positive; zero/negative records have unproven semantics.";
                if (opening.Quantity > FaithQuantityPlanning.MaximumTarget)
                    return "Imported quantity exceeds the conservative F6 ceiling; unchanged.";
                return current >= opening.Quantity ? "Already at or above the imported quantity; unchanged." : null;
            }
        }

        var resource = Resources.FirstOrDefault(candidate => candidate.ActionId == actionId);
        if (resource is null) return Error(actionId, "Unknown planned action.");
        var matches = inventory.Items.Where(item => item.Id == resource.ItemId).ToArray();
        if (matches.Length != 1 || _openingCounts.GetValueOrDefault(resource.ItemId) != 1 ||
            !_opening.TryGetValue(matches[0].Slot, out var original) || original.ItemId != resource.ItemId ||
            catalog.Item(resource.ItemId) is not { Category: "resource" } record ||
            FaithCatalog.PlainText(record.Name) != resource.ReferenceName)
            return Error(actionId, "Resource requires one unchanged, unambiguous main-bank row and a qualified name/ID join.");
        var key = $"inventory/{matches[0].Slot}";
        if (scope.QueryScope is not null && (scope.QueryScope.CollectionId != FaithInventoryView.CollectionId || scope.RowKeys is null) ||
            scope.RowKeys is { } keys && (keys.Count > 1 || keys.Any(selected => selected != key)))
            return Error(actionId, "Resource scope must contain only its main-bank row.");
        var result = quantities.Plan(FaithQuantityPlanning.ActionId, parameters,
            new ActionScope(scope.RowKeys is { Count: 0 } ? [] : [key]));
        return result with
        {
            Changes = result.Changes.Select(change => change.Derived ? change : change with { Label = $"{resource.Label} balance — main slot {matches[0].Slot}" }).ToArray(),
            Warnings = [.. result.Warnings, "Resource storage Verified; name/ID join Inferred. " + catalog.Provenance(resource.ItemId),
                FaithStackPolicy.Warning(resource.ItemId, "resource")],
        };
    }

    // Deliberately narrow: excludes Last Elixir/Elixir, rare boss drops and progression/upgrade materials.
    private static bool IsOrdinary(int id) => id is 5 or 6 or 8 or 9 or 10 or >= 101 and <= 109;
    private static bool QualifiedOrdinary(FaithCatalog catalog, int id) =>
        catalog.Item(id) is { } record && record.Category == (id >= 101 ? "material" : "consumable");
    private static ActionPlan Error(string id, string message) => new() { Errors = [new(id, message)] };
    private sealed record Resource(string ActionId, int ItemId, string Label, string ReferenceName);
    private sealed record OpeningStack(int ItemId, int Quantity);
}
