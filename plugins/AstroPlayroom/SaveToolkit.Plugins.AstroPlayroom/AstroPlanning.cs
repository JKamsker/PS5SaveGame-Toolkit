using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal static class AstroPlanning
{
    public const string SetCoinsAction = "common/set-coins";
    public const string CollectAllAction = "collectibles/collect-all";

    private const string DisabledCollectionWarning =
        "Collectible writes are disabled: mixed-event generations do not prove pickup dependencies, cinematic state, notifications or UDS counters.";

    public static IReadOnlyList<ActionNode> Views(long coins) => [
        new ActionNode(SetCoinsAction, "Set coins (review before apply)")
        {
            SupportsPlanning = true,
            Tone = Tone.Warning,
            Description = "F6 preview changes only Common/NumCoin. The 0→1749 differential and byte-identical round trips prove serialization, not the game's accepted cap or downstream effects.",
            Parameters = [new FieldNode("amount", "Target coins", new IntegerSpec(0, int.MaxValue), FieldValue.Of(coins))]
        },
        new ActionNode(CollectAllAction, "Plan collect all existing records")
        {
            SupportsPlanning = true,
            Tone = Tone.Warning,
            Description = "F6 preview only. Every collectible row is currently skipped until controlled single-event captures prove a complete dependency group; no collectible write is enabled."
        }
    ];

    public static ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
        ActionScope scope, AstroSchema schema, IReadOnlyList<AstroField> fields,
        Func<AstroField, FieldValue> current)
    {
        return actionId switch
        {
            SetCoinsAction => PlanCoins(parameters, scope, fields, current),
            CollectAllAction => PlanCollectAll(scope, schema, current),
            _ => new ActionPlan { Errors = [new FieldError(actionId, "Unknown ASTRO planning action.")] }
        };
    }

    public static EditOutcome Apply(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
        ActionScope scope, ActionPlan plan, AstroSchema schema, IReadOnlyList<AstroField> fields,
        Func<AstroField, FieldValue> current, Func<IReadOnlyList<ValueEdit>, EditOutcome> applyEdits)
    {
        var fresh = Plan(actionId, parameters, scope, schema, fields, current);
        if (fresh.Errors.Count > 0)
        {
            return new EditOutcome { Errors = fresh.Errors };
        }

        if (!Equivalent(fresh, plan))
        {
            return EditOutcome.Rejected(actionId, "The ASTRO preview is stale or no longer matches the document.");
        }

        if (actionId == CollectAllAction)
        {
            return new EditOutcome { Warnings = fresh.Warnings };
        }

        var edits = fresh.Changes.Where(c => !c.Derived)
            .Select(c => new ValueEdit(c.ValueId, c.NewValue)).ToArray();
        return applyEdits(edits) with { Warnings = fresh.Warnings };
    }

    private static ActionPlan PlanCoins(IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope,
        IReadOnlyList<AstroField> fields, Func<AstroField, FieldValue> current)
    {
        if (scope.RowKeys is { Count: > 0 })
        {
            return new ActionPlan { Errors = [new FieldError(SetCoinsAction, "Coin edits are document-wide; do not supply collectible row keys.")] };
        }

        if (!parameters.TryGetValue("amount", out var amount) || amount.Kind != FieldValueKind.Integer ||
            amount.AsInteger() < Int128.Zero || amount.AsInteger() > (Int128)int.MaxValue)
        {
            return new ActionPlan { Errors = [new FieldError("amount", "Target coins must be between 0 and 2,147,483,647.")] };
        }

        var coins = fields.SingleOrDefault(f => f.Path == "PreInstall/Common/NumCoin");
        if (coins is null)
        {
            return new ActionPlan { Errors = [new FieldError(SetCoinsAction, "This schema has no verified Common/NumCoin field.")] };
        }

        var oldValue = current(coins);
        var change = new PlannedChange(coins.Token.Id, "Coins (Common/NumCoin)", oldValue, amount)
        {
            Derived = false
        };
        IReadOnlyList<string> warnings = oldValue == amount
            ? ["Target already equals the saved coin value."]
            : ["Experimental: console acceptance, coin cap and linked gameplay effects are unverified."];
        return new ActionPlan { Changes = oldValue == amount ? [] : [change], Warnings = warnings };
    }

    private static ActionPlan PlanCollectAll(ActionScope scope, AstroSchema schema,
        Func<AstroField, FieldValue> current)
    {
        var records = schema.Records.Where(r => r.Group is "Artifact" or "Puzzle" or "Prop");
        if (scope.RowKeys is { } selected)
        {
            var selectedSet = selected.ToHashSet(StringComparer.Ordinal);
            records = records.Where(r => selectedSet.Contains(r.Element.Id));
        }

        var skipped = records.Select(record => new SkippedActionRow(record.Element.Id,
            record.Ownership is { } ownership && current(ownership).AsBool()
                ? "Already owned; no change proposed."
                : "Collectible writes are disabled pending a controlled single-event dependency capture."))
            .ToArray();
        return new ActionPlan { SkippedRows = skipped, Warnings = [DisabledCollectionWarning] };
    }

    private static bool Equivalent(ActionPlan left, ActionPlan right) =>
        left.Errors.SequenceEqual(right.Errors) && left.Warnings.SequenceEqual(right.Warnings) &&
        left.SkippedRows.SequenceEqual(right.SkippedRows) && left.Changes.SequenceEqual(right.Changes);
}
