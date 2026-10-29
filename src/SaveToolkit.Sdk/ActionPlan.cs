using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Sdk;

/// <summary>Optional capability for actions whose effects must be reviewed before applying.</summary>
public interface IActionPlanningDocument
{
    /// <summary>
    /// Pure validation and calculation, including derived effects. Must not mutate the document.
    /// Null row keys without a query means document scope; an empty list means no selected rows.
    /// Core resolves query scopes to bounded row keys before calling the plugin.
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

/// <summary>
/// Selection for a planned action. A null scope, including an omitted HTTP scope, means document
/// scope; a non-null empty row-key list means that no rows are selected. Query scopes are resolved by
/// Core and the returned scope contains the normalized selection.
/// </summary>
public sealed record ActionScope(IReadOnlyList<string>? RowKeys = null)
{
    /// <summary>Optional whole-query selection. Core supplies both this and the resolved RowKeys to plugins.</summary>
    public ActionQueryScope? QueryScope { get; init; }
}

/// <summary>A revision-bound selection of every matching row, including pages never loaded by the client.</summary>
public sealed record ActionQueryScope(string CollectionId, QueryableCollectionQuery Query, long Revision)
{
    public IReadOnlyList<string> ExcludedRowKeys { get; init; } = [];
}

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
