using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed class StackEditor(CsavContainer container, CsavTree tree, IReadOnlyList<InventoryItem> items, byte[] payload)
{
    private const uint MaximumStack = 999_999; // Editor policy, not a proven game limit.
    private const uint MaximumMoney = 100_000_000;
    public const string SetTarget = "inventory/set-target";
    public const string Refill = "inventory/refill-original";
    private readonly ItemNames _names = new();
    private readonly InventoryItem? _money = FindMoney(items, tree);

    public bool CanEdit => items.Any(IsWritable);
    public InventoryItem? Money => _money;
    public static bool IsAction(string id) => id is SetTarget or Refill;
    private bool IsWritable(InventoryItem item) => item == _money || item.ContainerId == 1 &&
        item.QuantityOffset.HasValue && tree.Nodes[item.NodeId].Size == 29 && item.Structure == 0 &&
        _names.Find(item.TweakId)?.Kind is "Ammo" or "Component" or "Consumable";

    public FieldNode Field(InventoryItem item, Func<ScalarBinding, FieldNode> bind)
    {
        var offset = item.QuantityOffset!.Value;
        var writable = IsWritable(item);
        var maximum = Math.Max(item == _money ? MaximumMoney : MaximumStack, BinaryData.UInt32(container.Payload, offset));
        return bind(new($"inventory/{item.NodeId}/quantity", item == _money ? "Money (Items.money)" : "Quantity",
            writable ? IntegerSpec.UInt32 with { Max = maximum } : IntegerSpec.UInt32,
            () => FieldValue.Of((ulong)BinaryData.UInt32(payload, offset)),
            writable ? value => BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset), value.AsUInt32()) : null,
            value => value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > maximum
                ? "Quantity is outside the editor's supported range." : null)) with
        {
            Confidence = writable ? Confidence.Inferred : Confidence.Verified,
            Description = CyberpunkReferences.BaselineAttribution + " Based on the parsed PS5 2.21 quantity-only record. " + WriteSafety.Warning,
            Warning = writable ? WriteSafety.Warning + " The editor cap is a policy, not a verified game limit." : null,
        };
    }

    public IReadOnlyList<ViewNode> Actions() => CanEdit ? [
        new ActionNode(SetTarget, "Set supported stack quantities")
        {
            SupportsPlanning = true,
            Description = "Selected supported existing stacks, or all supported stacks in document scope. Money is excluded. " + WriteSafety.Warning,
            Parameters = [new FieldNode("quantity", "Target quantity (editor policy cap)", new IntegerSpec(0, MaximumStack), FieldValue.Of(1L))],
        },
        new ActionNode(Refill, "Refill stacks to opening quantities")
        {
            SupportsPlanning = true,
            Description = "Restore supported stacks below their original quantity; never reduce a stack or change money. " + WriteSafety.Warning,
        }] : [];

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        ActionPlan Reject(string message) => new() { Errors = [new FieldError(actionId, message)], Warnings = [WriteSafety.Warning] };
        if (!IsAction(actionId)) return Reject("Unknown planned action.");
        if (scope.QueryScope is { } query && (query.CollectionId != InventoryInspection.CollectionId || scope.RowKeys is null))
            return Reject("The action requires an inventory scope with server-resolved row keys.");
        if (parameters.Keys.Any(key => actionId != SetTarget || key != "quantity")) return Reject("Unknown action parameter.");
        FieldValue target = default;
        if (actionId == SetTarget && (!parameters.TryGetValue("quantity", out target) || target.Kind != FieldValueKind.Integer ||
            target.AsInteger() < 0 || target.AsInteger() > MaximumStack)) return Reject("Target quantity must be an integer within the editor policy range.");
        var byKey = items.ToDictionary(item => item.NodeId.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal);
        var keys = scope.RowKeys ?? byKey.Keys.ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count || keys.Any(key => !byKey.ContainsKey(key)))
            return Reject("Selection contains duplicate or unknown inventory rows.");
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        foreach (var key in keys)
        {
            var item = byKey[key];
            if (item == _money || !IsWritable(item))
            {
                skipped.Add(new(key, item == _money ? "Money is excluded from stack actions." : "Unsupported container, identity or item structure; preserved."));
                continue;
            }
            var original = BinaryData.UInt32(container.Payload, item.QuantityOffset!.Value);
            var current = BinaryData.UInt32(payload, item.QuantityOffset.Value);
            var next = actionId == Refill ? Math.Max(original, current) : target.AsUInt32();
            if (current == next) { skipped.Add(new(key, "Already at target (refill never reduces a stack).")); continue; }
            changes.Add(new($"inventory/{item.NodeId}/quantity", $"{_names.Find(item.TweakId)!.Name} quantity · node {item.NodeId}",
                FieldValue.Of((ulong)current), FieldValue.Of((ulong)next)));
        }
        return new ActionPlan { Changes = changes, SkippedRows = skipped,
            Warnings = [WriteSafety.Warning + " Only supported existing quantity fields change. Charges/replenishment and gameplay caps are unverified."] };
    }

    public void BindTargets(ActionPlan plan, Func<ScalarBinding, FieldNode> bind)
    {
        var ids = plan.Changes.Select(change => change.ValueId).ToHashSet(StringComparer.Ordinal);
        foreach (var item in items.Where(item => ids.Contains($"inventory/{item.NodeId}/quantity"))) _ = Field(item, bind);
    }

    private static InventoryItem? FindMoney(IReadOnlyList<InventoryItem> entries, CsavTree nodes)
    {
        var candidates = entries.Where(item => item.ContainerId == 1 && item.TweakId == InventoryReader.MoneyTweakId &&
            item.QuantityOffset.HasValue && nodes.Nodes[item.NodeId].Size == 29 && item.Structure == 0).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }
}
