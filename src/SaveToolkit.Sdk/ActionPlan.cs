using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Sdk;

/// <summary>Optional capability for actions whose effects must be reviewed before applying.</summary>
public interface IActionPlanningDocument
{
    /// <summary>
    /// Pure validation and calculation, including derived effects. Must not mutate the document.
    /// Null row keys means document scope; an empty list means no selected rows, never all rows.
    /// Called again at apply time. Results must be deterministic for the same document and request.
    /// Planning is allowed on read-only documents; applying is not.
    /// </summary>
    ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope);

    /// <summary>
    /// Applies the revalidated request atomically. On rejection or exception nothing may change.
    /// Report all changes, including derived values, and a Revert memento for structural changes.
    /// The framework records the result as one undo group. Do not retain or mutate the supplied plan.
    /// </summary>
    EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan);
}

public sealed record ActionScope(IReadOnlyList<string>? RowKeys = null);

/// <summary>Named, game-level consequences. Null before/after means absence only when the corresponding Exists flag is false.</summary>
public sealed record PlannedChange(string ValueId, string Label, FieldValue OldValue, FieldValue NewValue)
{
    public bool Derived { get; init; }
    public bool OldExists { get; init; } = true;
    public bool NewExists { get; init; } = true;
}

public sealed record SkippedActionRow(string RowKey, string Reason);

public sealed record ActionPlan
{
    public IReadOnlyList<PlannedChange> Changes { get; init; } = [];
    public IReadOnlyList<SkippedActionRow> SkippedRows { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<FieldError> Errors { get; init; } = [];
}
