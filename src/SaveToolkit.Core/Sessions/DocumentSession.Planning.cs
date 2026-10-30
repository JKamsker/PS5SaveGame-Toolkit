using System.Collections.ObjectModel;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Sessions.Groups;

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
    private Action? _startAtomicApply;

    public ActionPreview PlanAction(string actionId, IReadOnlyDictionary<string, JsonElement> parameters,
        ActionScope? scope = null, long? expectedRevision = null) =>
        PlanAction(actionId, parameters, scope, expectedRevision, CancellationToken.None);

    public ActionPreview PlanAction(string actionId, IReadOnlyDictionary<string, JsonElement> parameters,
        ActionScope? scope, long? expectedRevision, CancellationToken cancellationToken) => Locked(() =>
        RunPreflight(_ => PlanActionCore(actionId, parameters, scope, expectedRevision, cancellationToken), cancellationToken), cancellationToken);

    private ActionPreview PlanActionCore(string actionId, IReadOnlyDictionary<string, JsonElement> parameters,
        ActionScope? scope, long? expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _pendingAction = null;
        var requested = scope ?? new ActionScope();
        ActionPreview Reject(IReadOnlyList<FieldError> errors, bool stale = false) =>
            new(null, _revision, requested, new ActionPlan { Errors = errors }) { Stale = stale };

        if (_unusable)
            return Reject([new FieldError(actionId, "The document session is unusable; reopen it before planning or applying actions.")]);

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
        cancellationToken.ThrowIfCancellationRequested();
        if (errors.Count > 0)
        {
            return Reject(errors);
        }

        try
        {
            if (requested.QueryScope is { } queryScope)
            {
                requested = ResolveActionQuery(queryScope, cancellationToken);
            }

            var readOnlyValues = new ReadOnlyDictionary<string, FieldValue>(values);
            cancellationToken.ThrowIfCancellationRequested();
            var plan = planner is StagedGroupDocument group
                ? group.PlanForSession(actionId, readOnlyValues, requested, cancellationToken)
                : planner.PlanAction(actionId, readOnlyValues, requested);
            cancellationToken.ThrowIfCancellationRequested();
            var detachedPlan = ActionPlanSnapshots.Snapshot(plan);
            var bytes = BoundedJson.Serialize(detachedPlan, MaxPlanBytes);
            var requestBytes = BoundedJson.Serialize(readOnlyValues, MaxPlanBytes).LongLength
                + BoundedJson.Serialize(requested, MaxPlanBytes).LongLength;
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes.LongLength + requestBytes > MaxPlanBytes)
            {
                return Reject([new FieldError(actionId, "Preview is too large; narrow its scope.")]);
            }

            if (detachedPlan.Errors.Count > 0)
            {
                return new ActionPreview(null, _revision, requested, detachedPlan);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var metadata = FieldMetadataPlanning.Validate(_index, Document, detachedPlan, _referenceFields, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_index.HasFieldMetadata
                && bytes.LongLength + requestBytes + BoundedJson.Serialize(metadata, MaxPlanBytes).LongLength > MaxPlanBytes)
                return Reject([new FieldError(actionId, "Preview metadata is too large; narrow its scope.")]);
            cancellationToken.ThrowIfCancellationRequested();
            var id = Guid.NewGuid().ToString("N");
            _pendingAction = new PendingAction(id, _revision, actionId, readOnlyValues, requested, bytes, metadata,
                detachedPlan.Changes.Any(change => change.Effect is not null));
            return new ActionPreview(id, _revision, requested, detachedPlan);
        }
        catch (StaleViewException ex)
        {
            return Reject([new FieldError(actionId, ex.Message)], stale: true);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("bounded budget", StringComparison.Ordinal)
            || ex.Message.Contains("bounded entry limit", StringComparison.Ordinal))
        {
            return Reject([new FieldError(actionId, "Preview is too large; narrow its scope.")]);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            return Reject([new FieldError(actionId, $"Planning failed: {ex.Message}")]);
        }
    }

    public SessionEditResult ApplyActionPlan(string planId, long expectedRevision) =>
        ApplyActionPlan(planId, expectedRevision, CancellationToken.None);

    public SessionEditResult ApplyActionPlan(string planId, long expectedRevision, CancellationToken cancellationToken) => Locked(() =>
        RunPreflight(startApply => ApplyActionPlanCore(planId, expectedRevision, startApply, cancellationToken), cancellationToken), cancellationToken);

    private SessionEditResult ApplyActionPlanCore(string planId, long expectedRevision,
        Action startApply, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_unusable)
            return Rejected(string.Empty, "The document session is unusable; reopen it before planning or applying actions.");
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

        try
        {
            if (pending.Scope.QueryScope is { } queryScope
                && !pending.Scope.RowKeys!.SequenceEqual(ResolveActionQuery(queryScope, cancellationToken).RowKeys!, StringComparer.Ordinal))
            {
                return Rejected(pending.ActionId, "The query selection changed. Preview it again.") with { Stale = true };
            }

            cancellationToken.ThrowIfCancellationRequested();
            var fresh = planner is StagedGroupDocument group
                ? group.PlanForSession(pending.ActionId, pending.Parameters, pending.Scope, cancellationToken)
                : planner.PlanAction(pending.ActionId, pending.Parameters, pending.Scope);
            cancellationToken.ThrowIfCancellationRequested();
            // Apply uses the same detached graph admission as preview. This keeps provider-owned
            // nested specs/presentation/path lists from bypassing bounds or changing while checked.
            var detachedFresh = ActionPlanSnapshots.Snapshot(fresh);
            if (detachedFresh.Errors.Count > 0)
                return new SessionEditResult { Errors = detachedFresh.Errors, Warnings = detachedFresh.Warnings, State = StateUnlocked() };

            cancellationToken.ThrowIfCancellationRequested();
            var metadata = FieldMetadataPlanning.Validate(_index, Document, detachedFresh, _referenceFields, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!FieldMetadataPlanning.Agree(pending.Metadata, metadata)
                || !pending.PlanBytes.AsSpan().SequenceEqual(BoundedJson.Serialize(detachedFresh, MaxPlanBytes)))
            {
                return Rejected(pending.ActionId, "The action's effects changed. Preview it again.") with { Stale = true };
            }

            // Point of no return: the plugin mutation and its undo/revision bookkeeping must
            // finish together. The request token is deliberately not observed after this check.
            cancellationToken.ThrowIfCancellationRequested();
            var effectDocument = pending.HasEffects ? Document as IActionEffectDocument : null;
            if (pending.HasEffects && (effectDocument is null
                || effectDocument is StagedGroupDocument staged && !staged.EffectEnabled))
                return Rejected(pending.ActionId, "This document no longer supports action effects.");
            DocumentMemento? before = null;
            var restoreAttempted = false;
            var historyRecorded = false;
            if (effectDocument is not null)
            {
                before = effectDocument.CaptureMemento();
                CheckSnapshotBudget(before);
            }
            var canonicalIds = pending.HasEffects
                ? detachedFresh.Changes.Where(change => change.Effect is null)
                    .Select(change => change.ValueId).ToHashSet(StringComparer.Ordinal)
                : null;
            _semantic.CaptureTargetedOriginals(_openOriginal,
                detachedFresh.Changes.Where(change => change.Effect is null).Select(change => change.ValueId), cancellationToken);
            startApply();
            return RunGroup(_ =>
            {
                try
                {
                    var outcome = planner.ApplyActionPlan(pending.ActionId, pending.Parameters, pending.Scope, detachedFresh);
                    if (!outcome.Succeeded)
                    {
                        restoreAttempted = true;
                        RestoreFailedEffect(effectDocument, before);
                        return FromOutcome(outcome);
                    }
                    if (pending.HasEffects)
                    {
                        ActionEffectOutcomeValidation.Validate(detachedFresh, outcome);
                        if (outcome.Revert is { } providerRevert) CheckSnapshotBudget(providerRevert);
                        var after = effectDocument!.CaptureMemento();
                        CheckSnapshotBudget(after);
                    }
                    var warnings = pending.HasEffects
                        ? Record(new MementoStep(before!))
                        : outcome.Revert is { } revert ? Record(new MementoStep(revert))
                        : outcome.Changes.Count > 0 ? Record(new ValueStep(outcome.Changes)) : [];
                    var semanticChanges = pending.HasEffects
                        ? outcome.Changes.Where(change => canonicalIds!.Contains(change.ValueId)).ToArray()
                        : outcome.Changes;
                    historyRecorded = pending.HasEffects || outcome.Revert is not null || outcome.Changes.Count > 0;
                    return AfterChange(FromOutcome(outcome), pending.HasEffects || outcome.StructureChanged || outcome.Revert is not null,
                        warnings, semanticChanges, pending.HasEffects);
                }
                catch
                {
                    if (!historyRecorded && !restoreAttempted)
                    {
                        restoreAttempted = true;
                        RestoreFailedEffect(effectDocument, before);
                    }
                    throw;
                }
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            var warnings = _unusable ? [] : RebuildIndex(forceNewRevision: false);
            return Rejected(pending.ActionId, $"Action failed: {ex.Message}") with { Warnings = warnings, State = StateUnlocked() };
        }
    }

    private void CheckSnapshotBudget(DocumentMemento memento)
    {
        if (memento.SizeBytes < 0 || memento.SizeBytes > _maxSnapshotBytes)
            throw new InvalidOperationException("The action snapshot exceeds its undo budget.");
    }

    private void RestoreFailedEffect(IActionEffectDocument? capability, DocumentMemento? before)
    {
        if (capability is null || before is null) return;
        try
        {
            _ = Document.RestoreMemento(before);
            var warnings = RebuildIndex(forceNewRevision: false);
            if (warnings.Count > 0) throw new InvalidOperationException(string.Join("; ", warnings));
        }
        catch (Exception restore) when (restore is not OutOfMemoryException)
        {
            MarkUnusable();
            throw new InvalidOperationException("Action failed and its snapshot could not restore the document; reopen it before continuing.", restore);
        }
    }

    // Shared by edits, actions and planning: host caches/preview publication are transactional
    // until the final token check. Plugin mutation and history must then complete together.
    private T RunPreflight<T>(Func<Action, T> operation, CancellationToken cancellationToken)
    {
        // Legacy callers cannot cancel. Retain their allocation profile unless they are
        // nested inside a cancellable operation whose atomic boundary must be propagated.
        if (!cancellationToken.CanBeCanceled && _startAtomicApply is null)
            return operation(static () => { });
        var previous = _pendingAction;
        var previousIndex = _index;
        var previousExpanded = new HashSet<string>(_expanded, StringComparer.Ordinal);
        var restoreGroup = (Document as StagedGroupDocument)?.CancellationRollback();
        var restoreSemantic = _semantic.CancellationRollback();
        _index = _index.Copy(cancellationToken);
        var applying = false;
        var parentStartApply = _startAtomicApply;
        void StartApply()
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Nested revert/group preflight crosses the same atomic boundary as its edit.
            parentStartApply?.Invoke();
            applying = true;
        }
        _startAtomicApply = StartApply;
        try
        {
            var result = operation(StartApply);
            if (!applying) cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (!applying)
        {
            _pendingAction = previous;
            _index = previousIndex;
            _expanded = previousExpanded;
            restoreGroup?.Invoke();
            restoreSemantic();
            throw;
        }
        finally { _startAtomicApply = parentStartApply; }
    }

    private sealed record PendingAction(string Id, long Revision, string ActionId,
        IReadOnlyDictionary<string, FieldValue> Parameters, ActionScope Scope, byte[] PlanBytes,
        IReadOnlyList<FieldMetadata> Metadata, bool HasEffects);

    private ActionScope ResolveActionQuery(ActionQueryScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (scope.Revision != _revision) throw new StaleViewException();
        ArgumentNullException.ThrowIfNull(scope.Query);
        var collection = QueryableCollections.FirstOrDefault(collection => collection.Id == scope.CollectionId)
            ?? throw new KeyNotFoundException($"Unknown collection '{scope.CollectionId}'.");
        scope = scope with { Query = scope.Query with
        {
            IncludeEmpty = scope.Query.IncludeEmpty ?? collection.EmptyRows?.HideByDefault != true,
        } };
        return ActionQueryScopeResolver.Resolve(scope, _maxQueryActionRows,
            query => QueryCollection(scope.CollectionId, query, scope.Revision, cancellationToken), cancellationToken);
    }
}
