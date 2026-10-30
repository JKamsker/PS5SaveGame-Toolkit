using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using System.Text.Json.Serialization;

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
    /// <summary>Stable row keys to omit from the complete matching result, including unloaded pages.</summary>
    public IReadOnlyList<string> ExcludedRowKeys { get; init; } = [];
}

/// <summary>Named, game-level consequences. Null before/after means absence only when the corresponding Exists flag is false.</summary>
public sealed record PlannedChange(string ValueId, string Label, FieldValue OldValue, FieldValue NewValue)
{
    /// <summary>True for a consequence of another edit; the plugin decides which consequences require actual assignments.</summary>
    public bool Derived { get; init; }
    /// <summary>False when the field did not exist before the action, distinct from an existing null value.</summary>
    public bool OldExists { get; init; } = true;
    /// <summary>False when the action removes the field, distinct from assigning null.</summary>
    public bool NewExists { get; init; } = true;
    /// <summary>Optional action-owned metadata; null retains legacy plan wire bytes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ActionEffectDescriptor? Effect { get; init; }
}

/// <summary>A selected row left unchanged, with a user-facing explanation.</summary>
public sealed record SkippedActionRow(string RowKey, string Reason);

/// <summary>Deterministic preview of ordered consequences and diagnostics, calculated without mutating the document.</summary>
public sealed record ActionPlan
{
    /// <summary>Every proposed direct and derived consequence in deterministic order.</summary>
    public IReadOnlyList<PlannedChange> Changes { get; init; } = [];
    /// <summary>Selected rows that cannot or need not change, with their reasons.</summary>
    public IReadOnlyList<SkippedActionRow> SkippedRows { get; init; } = [];
    /// <summary>Limitations and notices to review before applying.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
    /// <summary>Validation failures that prevent the plan from being applied.</summary>
    public IReadOnlyList<FieldError> Errors { get; init; } = [];
}
