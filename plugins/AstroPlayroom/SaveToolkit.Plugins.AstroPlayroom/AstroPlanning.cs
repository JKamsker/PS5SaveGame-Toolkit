using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal static class AstroPlanning
{
    public const string SetCoinsAction = "common/set-coins";
    public const string CollectAllAction = "collectibles/collect-all";

    public static IReadOnlyList<ActionNode> Views(long? coins) => [
        .. (coins is { } value ? new[] { CoinAction(value), AstroGatchaPlanning.View() } : Array.Empty<ActionNode>()),
        CollectionAction(), AstroRestorePlanning.View()
    ];

    private static ActionNode CoinAction(long coins) =>
        new ActionNode(SetCoinsAction, "Set coins (review before apply)")
        {
            SupportsPlanning = true,
            Tone = Tone.Warning,
            Description = "F6 preview changes only Common/NumCoin. Observed range: 0–1749; the signed storage bound is not a proven game cap. " +
                AstroWriteWarnings.Experimental + " " + AstroWriteWarnings.Integrity,
            Parameters = [new FieldNode("amount", "Target coins", new IntegerSpec(0, int.MaxValue), FieldValue.Of(coins))]
        };

    public static ActionNode CollectionAction() =>
        new ActionNode(CollectAllAction, "Collect existing records (review before apply)")
        {
            SupportsPlanning = true,
            Tone = Tone.Warning,
            Description = "Outside a table, review all existing collectibles. In a table, review its selected rows (including hidden selections). " +
                AstroWriteWarnings.Experimental + " " + AstroWriteWarnings.Collection + " " + AstroWriteWarnings.Integrity
        };

    public static ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
        ActionScope scope, AstroSchema schema, IReadOnlyList<AstroField> fields,
        Func<AstroField, FieldValue> current, AstroLocations locations)
    {
        if (scope.QueryScope is { } query &&
            (!AstroCollectionQuery.KnownCollection(query.CollectionId) || scope.RowKeys is null))
        {
            return new ActionPlan { Errors = [new FieldError(actionId, "Collectible query scopes must be resolved to row keys by Core before planning.")] };
        }

        return actionId switch
        {
            SetCoinsAction => PlanCoins(parameters, scope, fields, current),
            AstroGatchaPlanning.ActionId => AstroGatchaPlanning.Plan(parameters, scope, fields, current),
            CollectAllAction => parameters.Count == 0 ? PlanCollectAll(scope, schema, current)
                : new ActionPlan { Errors = [new FieldError(actionId, "Collect existing records takes no parameters.")] },
            AstroWorldPlanning.ActionId => AstroWorldPlanning.Plan(parameters, scope, schema, locations, current),
            AstroRestorePlanning.ActionId => AstroRestorePlanning.Plan(parameters, scope, schema, current),
            _ => new ActionPlan { Errors = [new FieldError(actionId, "Unknown ASTRO planning action.")] }
        };
    }

    public static EditOutcome Apply(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
        ActionScope scope, ActionPlan plan, AstroSchema schema, IReadOnlyList<AstroField> fields,
        Func<AstroField, FieldValue> current, AstroLocations locations, Func<IReadOnlyList<ValueEdit>, EditOutcome> applyEdits)
    {
        var validation = ActionPlans.Revalidate(plan,
            () => Plan(actionId, parameters, scope, schema, fields, current, locations),
            new FieldError(actionId, "The ASTRO preview is stale or no longer matches the document."));
        if (!validation.Succeeded)
        {
            return new EditOutcome { Errors = validation.Errors };
        }

        var fresh = validation.Plan;
        var edits = fresh.Changes.Where(c => !c.Derived)
            .Select(c => new ValueEdit(c.ValueId, c.NewValue)).ToArray();
        return applyEdits(edits) with { Warnings = fresh.Warnings };
    }

    public static ActionPlan PlanCoins(IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope,
        IReadOnlyList<AstroField> fields, Func<AstroField, FieldValue> current)
    {
        if (scope.RowKeys is not null)
        {
            return new ActionPlan { Errors = [new FieldError(SetCoinsAction, "Coin edits are document-wide; do not supply collectible row keys.")] };
        }

        if (parameters.Count != 1 || !parameters.TryGetValue("amount", out var amount) || amount.Kind != FieldValueKind.Integer ||
            amount.AsInteger() < Int128.Zero || amount.AsInteger() > (Int128)int.MaxValue)
        {
            return new ActionPlan { Errors = [new FieldError("amount", "Target coins must be between 0 and 2,147,483,647.")] };
        }

        var coins = AstroFields.Coins(fields);
        if (coins is null)
        {
            return new ActionPlan { Errors = [new FieldError(SetCoinsAction, "This schema has no verified Common/NumCoin field.")] };
        }

        var oldValue = current(coins);
        var change = new PlannedChange(coins.Token.Id, "Coins (Common/NumCoin)", oldValue, amount)
        {
            Derived = false
        };
        IReadOnlyList<string> warnings = [AstroWriteWarnings.Experimental, AstroWriteWarnings.Integrity,
            "Only the 0–1749 coin range is observed; 0–2,147,483,647 is a storage policy, not a proven gameplay range.",
            .. (oldValue == amount ? new[] { "Target already equals the saved coin value." } : Array.Empty<string>())];
        return new ActionPlan { Changes = oldValue == amount ? [] : [change], Warnings = warnings };
    }

    public static ActionPlan PlanCollectAll(ActionScope scope, AstroSchema schema,
        Func<AstroField, FieldValue> current)
    {
        return schema.TrySelectRecords(scope.RowKeys, out var records) ? PlanRecords(records, current) : InvalidSelection();
    }

    public static ActionPlan InvalidSelection() => new()
    {
        Errors = [new FieldError(CollectAllAction, "Selection must contain distinct existing collectible row keys only.")]
    };

    public static ActionPlan PlanRecords(IEnumerable<AstroCollectionRecord> records, Func<AstroField, FieldValue> current)
    {
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        foreach (var record in records)
        {
            if (record.Ownership is not { Editable: true } ownership)
            {
                skipped.Add(new SkippedActionRow(record.Element.Id, "No unique editable boolean IsGot field; unknown structure is preserved."));
            }
            else if (current(ownership).AsBool())
            {
                skipped.Add(new SkippedActionRow(record.Element.Id, "Already owned; no change proposed."));
            }
            else
            {
                changes.Add(new PlannedChange(ownership.Token.Id, $"{record.Group}/{record.Element.Name} IsGot (Inferred)",
                    current(ownership), FieldValue.Of(true)));
            }
        }

        return new ActionPlan { Changes = changes, SkippedRows = skipped,
            Warnings = [AstroWriteWarnings.Experimental, AstroWriteWarnings.Collection, AstroWriteWarnings.Integrity] };
    }
}
