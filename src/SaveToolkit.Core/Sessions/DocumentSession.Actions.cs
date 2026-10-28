using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Sessions;

public sealed partial class DocumentSession
{
    /// <param name="actionId">Action id from an indexed <see cref="ActionNode"/>.</param>
    /// <param name="parameters">Parameter values as sent by the client; missing ones use the published defaults.</param>
    /// <param name="expectedRevision">The <see cref="DocumentState.Revision"/> the client's view was built at, if known.</param>
    public SessionEditResult InvokeAction(string actionId, IReadOnlyDictionary<string, JsonElement> parameters, long? expectedRevision = null) => Locked(() =>
    {
        if (StaleCheck(expectedRevision, [actionId]) is { } stale)
        {
            return stale;
        }

        if (_index.Action(actionId) is not { } action)
        {
            return Rejected(actionId, "Unknown action.");
        }

        if (!Document.CanEdit)
        {
            return Rejected(actionId, "This document is read-only.");
        }

        if (action.SupportsPlanning)
        {
            return Rejected(actionId, "This action requires a preview. Plan it before applying.");
        }

        var (values, errors) = CoerceParameters(action, parameters);
        if (errors.Count > 0)
        {
            return new SessionEditResult { Errors = errors, State = StateUnlocked() };
        }

        EnsureOriginal();
        ActionOutcome outcome;
        try
        {
            outcome = Document.InvokeAction(actionId, values);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A failed structural action rolls itself back, which may replace model objects and
            // drop bindings; re-index quietly (the revision only moves if the ids changed).
            var revision = _revision;
            var rebuildWarnings = RebuildIndex(forceNewRevision: false);
            return Rejected(actionId, $"Action failed: {ex.Message}") with
            {
                Warnings = rebuildWarnings,
                StructureChanged = revision != _revision,
                State = StateUnlocked(),
            };
        }

        IReadOnlyList<string> warnings = [];
        if (!outcome.Undoable)
        {
            ClearHistory();
        }
        else if (outcome.Revert is { } revert)
        {
            warnings = Record(new MementoStep(revert));
        }
        else if (outcome.Changes.Count > 0)
        {
            warnings = Record(new ValueStep(outcome.Changes));
        }

        var result = new SessionEditResult
        {
            Changes = outcome.Changes,
            Warnings = outcome.Warnings,
            Message = outcome.Message,
            StructureChanged = outcome.StructureChanged || !outcome.Undoable || outcome.Revert is not null,
            State = StateUnlocked(),
        };
        return AfterChange(result, result.StructureChanged, warnings);
    });

    private static (Dictionary<string, FieldValue> Values, List<FieldError> Errors) CoerceParameters(
        ActionNode action, IReadOnlyDictionary<string, JsonElement> parameters)
    {
        var values = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        var errors = new List<FieldError>();
        foreach (var p in action.Parameters)
        {
            var coerced = parameters.TryGetValue(p.ValueId, out var json)
                ? FieldValueCoercer.FromJson(json, p.Spec)
                : FieldValueCoercer.Normalize(p.Value, p.Spec, allowReadOnly: true);
            if (coerced.Ok)
            {
                values[p.ValueId] = coerced.Value;
            }
            else
            {
                errors.Add(new FieldError(p.ValueId, coerced.Error!));
            }
        }

        return (values, errors);
    }
}
