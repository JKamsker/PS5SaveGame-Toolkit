using System.Collections.ObjectModel;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Sessions;

public sealed record ActionPreview(string? PlanId, long Revision, ActionScope Scope, ActionPlan Plan)
{
    public bool Stale { get; init; }
    public string? Code => Stale ? "stale" : null;
}

public sealed partial class DocumentSession
{
    // Only the latest preview is retained. Neither client-supplied consequences nor tokens from
    // another document can ever be applied. Replacement and a size limit bound retained memory.
    private const int MaxPlanBytes = 4 * 1024 * 1024;
    private const int MaxScopeRows = 5000;
    private readonly int _maxQueryActionRows;
    private PendingAction? _pendingAction;

    public ActionPreview PlanAction(string actionId, IReadOnlyDictionary<string, JsonElement> parameters,
        ActionScope? scope = null, long? expectedRevision = null) => Locked(() =>
    {
        _pendingAction = null;
        var requested = scope ?? new ActionScope();
        ActionPreview Reject(IReadOnlyList<FieldError> errors, bool stale = false) =>
            new(null, _revision, requested, new ActionPlan { Errors = errors }) { Stale = stale };

        if (StaleCheck(expectedRevision, [actionId]) is { } stale)
        {
            return Reject(stale.Errors, stale: true);
        }

        if (_index.Action(actionId) is not { SupportsPlanning: true } action || Document is not IActionPlanningDocument planner)
        {
            return Reject([new FieldError(actionId, "This action does not support planning.")]);
        }

        if (!action.Mutates)
        {
            return Reject([new FieldError(actionId, "Inspection actions must use direct invocation, without planning.")]);
        }

        if (requested.QueryScope is not null && requested.RowKeys is not null)
        {
            return Reject([new FieldError(actionId, "Supply either row keys or a query scope, not both.")]);
        }

        if (requested.RowKeys is { } keys && (keys.Count > MaxScopeRows || keys.Any(string.IsNullOrEmpty) || keys.Distinct(StringComparer.Ordinal).Count() != keys.Count))
        {
            return Reject([new FieldError(actionId, "Selection must contain at most 5000 distinct row keys.")]);
        }

        // Copy request data; callers cannot change the retained request after seeing the preview.
        requested = requested with { RowKeys = requested.RowKeys is { } rows ? Array.AsReadOnly(rows.ToArray()) : null };
        var (values, errors) = CoerceParameters(action, parameters);
        if (errors.Count > 0)
        {
            return Reject(errors);
        }

        try
        {
            if (requested.QueryScope is { } queryScope)
            {
                requested = ResolveActionQuery(queryScope);
            }

            var readOnlyValues = new ReadOnlyDictionary<string, FieldValue>(values);
            var plan = planner.PlanAction(actionId, readOnlyValues, requested);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(plan);
            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(readOnlyValues).LongLength
                + JsonSerializer.SerializeToUtf8Bytes(requested).LongLength;
            if (bytes.LongLength + requestBytes > MaxPlanBytes)
            {
                return Reject([new FieldError(actionId, "Preview is too large; narrow its scope.")]);
            }

            if (plan.Errors.Count > 0)
            {
                return new ActionPreview(null, _revision, requested, plan);
            }

            var id = Guid.NewGuid().ToString("N");
            _pendingAction = new PendingAction(id, _revision, actionId, readOnlyValues, requested, bytes);
            return new ActionPreview(id, _revision, requested, plan);
        }
        catch (StaleViewException ex)
        {
            return Reject([new FieldError(actionId, ex.Message)], stale: true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Reject([new FieldError(actionId, $"Planning failed: {ex.Message}")]);
        }
    });

    public SessionEditResult ApplyActionPlan(string planId, long expectedRevision) => Locked(() =>
    {
        var pending = _pendingAction;
        _pendingAction = null; // Single use, including failed validation.
        if (pending is null || pending.Id != planId || pending.Revision != expectedRevision || expectedRevision != _revision)
        {
            return Rejected(string.Empty, "The preview is stale. Preview the action again.") with { Stale = true };
        }

        if (!Document.CanEdit)
        {
            return Rejected(pending.ActionId, "This document is read-only.");
        }

        if (Document is not IActionPlanningDocument planner)
        {
            return Rejected(pending.ActionId, "This document does not support planning.");
        }

        EnsureOriginal();
        try
        {
            if (pending.Scope.QueryScope is { } queryScope
                && !pending.Scope.RowKeys!.SequenceEqual(ResolveActionQuery(queryScope).RowKeys!, StringComparer.Ordinal))
            {
                return Rejected(pending.ActionId, "The query selection changed. Preview it again.") with { Stale = true };
            }

            var fresh = planner.PlanAction(pending.ActionId, pending.Parameters, pending.Scope);
            if (fresh.Errors.Count > 0)
            {
                return new SessionEditResult { Errors = fresh.Errors, Warnings = fresh.Warnings, State = StateUnlocked() };
            }

            if (!pending.PlanBytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(fresh)))
            {
                return Rejected(pending.ActionId, "The action's effects changed. Preview it again.") with { Stale = true };
            }

            return RunGroup(_ =>
            {
                var outcome = planner.ApplyActionPlan(pending.ActionId, pending.Parameters, pending.Scope, fresh);
                if (!outcome.Succeeded)
                {
                    return FromOutcome(outcome);
                }

                var warnings = outcome.Revert is { } revert ? Record(new MementoStep(revert))
                    : outcome.Changes.Count > 0 ? Record(new ValueStep(outcome.Changes)) : [];
                return AfterChange(FromOutcome(outcome), outcome.StructureChanged || outcome.Revert is not null, warnings);
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var warnings = RebuildIndex(forceNewRevision: false);
            return Rejected(pending.ActionId, $"Action failed: {ex.Message}") with { Warnings = warnings, State = StateUnlocked() };
        }
    });

    private sealed record PendingAction(string Id, long Revision, string ActionId,
        IReadOnlyDictionary<string, FieldValue> Parameters, ActionScope Scope, byte[] PlanBytes);

    private ActionScope ResolveActionQuery(ActionQueryScope scope)
    {
        if (scope.Revision != _revision) throw new StaleViewException();
        ArgumentNullException.ThrowIfNull(scope.Query);
        var collection = QueryableCollections.FirstOrDefault(collection => collection.Id == scope.CollectionId)
            ?? throw new KeyNotFoundException($"Unknown collection '{scope.CollectionId}'.");
        scope = scope with { Query = scope.Query with
        {
            IncludeEmpty = scope.Query.IncludeEmpty ?? collection.EmptyRows?.HideByDefault != true,
        } };
        return ActionQueryScopeResolver.Resolve(scope, _maxQueryActionRows,
            query => QueryCollection(scope.CollectionId, query, scope.Revision));
    }
}
