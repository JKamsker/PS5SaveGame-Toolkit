using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>Both writers require the complete ordered preview to match their freshly generated plan.</summary>
internal static class QuantityPlanComparison
{
    public static bool Matches(ActionPlan expected, ActionPlan supplied) =>
        expected.Errors.Count == 0 && supplied.Errors.Count == 0 &&
        expected.Changes.SequenceEqual(supplied.Changes) && expected.SkippedRows.SequenceEqual(supplied.SkippedRows) &&
        expected.Warnings.SequenceEqual(supplied.Warnings);
}
