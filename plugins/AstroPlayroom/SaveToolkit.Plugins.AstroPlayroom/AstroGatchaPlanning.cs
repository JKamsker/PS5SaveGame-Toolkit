using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal static class AstroGatchaPlanning
{
    public const string ActionId = "common/top-up-gatcha";
    private const string AttemptsParameter = "attempts";
    private const int CoinsPerAttempt = 100;
    private const int ObservedCoinCeiling = 1749;
    private const int MaximumAttempts = ObservedCoinCeiling / CoinsPerAttempt;
    // Factual cost only, independently implemented arithmetic; no guide prose/media copied.
    private const string Evidence = "Budget assumes 100 coins per attempt, reported by Gage / PowerPyx (2020-11-17; rechecked 2026-10-28): " +
        "https://www.powerpyx.com/astros-playroom-all-collectible-locations-artefacts-puzzle-pieces/ . " +
        "No guide prose or media is embedded. Random prizes do not guarantee collection completion. " +
        "0–17 attempts keeps the calculated target within the sample-observed 0–1749 coin range, not a proven game cap. Existing higher balances are preserved.";

    public static ActionNode View() => new(ActionId, "Top up coin budget for gatcha attempts")
    {
        SupportsPlanning = true,
        Tone = Tone.Warning,
        Description = Evidence + " " + AstroWriteWarnings.Experimental + " " + AstroWriteWarnings.Integrity,
        Parameters = [new FieldNode(AttemptsParameter, "Planned attempts", new IntegerSpec(0, MaximumAttempts), FieldValue.Of(1L))
        { Confidence = Confidence.Inferred, Description = Evidence }]
    };

    public static ActionPlan Plan(IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope,
        IReadOnlyList<AstroField> fields, Func<AstroField, FieldValue> current)
    {
        if (parameters.Count != 1 || !parameters.TryGetValue(AttemptsParameter, out var attempts) ||
            attempts.Kind != FieldValueKind.Integer || attempts.AsInteger() < 0 || attempts.AsInteger() > MaximumAttempts)
            return new ActionPlan { Errors = [new FieldError(AttemptsParameter, $"Planned attempts must be between 0 and {MaximumAttempts}.")] };
        if (AstroFields.Coins(fields) is not { } coins)
            return new ActionPlan { Errors = [new FieldError(ActionId, "This schema has no unique editable Common/NumCoin integer.")] };

        // Validate before conversion/multiplication. This is a top-up, never an addition to the
        // current balance; repeated previews/applications cannot repeatedly award the budget.
        var budget = checked((long)attempts.AsInteger() * CoinsPerAttempt);
        var target = Math.Max(current(coins).AsInt64(), budget);
        var plan = AstroPlanning.PlanCoins(new Dictionary<string, FieldValue> { ["amount"] = FieldValue.Of(target) }, scope, fields, current);
        return plan with { Warnings = [.. plan.Warnings, Evidence] };
    }
}
