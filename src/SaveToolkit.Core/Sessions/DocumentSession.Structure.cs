using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>
/// History replay (value steps and structural mementos), undo groups, and view revisions:
/// after a structural change the view index is rebuilt and ids that vanished are retired, so
/// clients still holding them get a typed "stale" rejection instead of touching the wrong record.
/// </summary>
public sealed partial class DocumentSession
{
    private const int MaxRetiredIds = 100_000;

    private readonly HashSet<string> _retired = new(StringComparer.Ordinal);
    private HashSet<string> _expanded = new(StringComparer.Ordinal);
    private long _revision;
    // Revision of the last change that altered view structure (ids/rows). Value edits advance
    // _revision for F6 plan staleness but leave structural views such as F8 pages valid.
    private long _structuralRevision;
    private List<HistoryStep>? _group;
    private bool _groupBroken;

    /// <summary>
    /// Runs <paramref name="body"/> (which calls this session's edit/action methods) as one undo entry.
    /// If it throws or returns a failed result, every change it made is reverted and nothing is recorded.
    /// Nested calls join the outer group.
    /// </summary>
    public SessionEditResult RunGroup(Func<DocumentSession, SessionEditResult> body) => Locked(() =>
    {
        if (_group is not null)
        {
            return body(this);
        }

        _group = [];
        _groupBroken = false;
        SessionEditResult result;
        try
        {
            result = body(this);
        }
        catch
        {
            RollBackGroup(TakeGroup());
            throw;
        }

        var steps = TakeGroup();
        if (!result.Ok)
        {
            var rollback = RollBackGroup(steps);
            return result with { Warnings = [.. result.Warnings, .. rollback.Warnings], StructureChanged = result.StructureChanged || rollback.StructureChanged, State = StateUnlocked() };
        }

        IReadOnlyList<string> warnings = steps.Count > 0 ? _history.Record(new HistoryEntry(steps)) : [];
        return result with { Warnings = [.. result.Warnings, .. warnings], State = StateUnlocked() };
    });

    private List<HistoryStep> TakeGroup()
    {
        var steps = _group ?? [];
        _group = null;
        return steps;
    }

    private SessionEditResult RollBackGroup(List<HistoryStep> steps)
    {
        if (_groupBroken)
        {
            return new SessionEditResult
            {
                Warnings = ["The operation failed after a step that cannot be undone; its earlier changes remain applied."],
                State = StateUnlocked(),
            };
        }

        if (steps.Count == 0)
        {
            return new SessionEditResult { State = StateUnlocked() };
        }

        var run = HistoryReplay.Run(steps, undo: true, ApplyStep);
        IReadOnlyList<string> warnings = [];
        if (!run.Outcome.Succeeded)
        {
            // The group stays applied but cannot be taken back step by step any more.
            _history.Clear(irreversible: true);
            warnings = ["The failed operation could not be fully reverted; undo history was cleared."];
        }

        return AfterChange(new SessionEditResult { StructureChanged = run.Outcome.StructureChanged, State = StateUnlocked() }, run.NeedsRebuild, warnings);
    }

    private SessionEditResult? StaleCheck(long? expectedRevision, IEnumerable<string> ids)
    {
        var stale = expectedRevision is { } r && r != _revision
            ? ids.FirstOrDefault() ?? string.Empty
            : ids.FirstOrDefault(id => _retired.Contains(id) && !_index.KnowsBinding(id));
        return stale is null
            ? null
            : new SessionEditResult { Errors = [new FieldError(stale, StaleViewException.DefaultMessage)], Stale = true, State = StateUnlocked() };
    }

    private IReadOnlyList<string> Record(HistoryStep step)
    {
        if (_group is not null)
        {
            _group.Add(step);
            return [];
        }

        return _history.Record(new HistoryEntry([step]));
    }

    private void ClearHistory()
    {
        _history.Clear(irreversible: true);
        if (_group is not null)
        {
            _group.Clear();
            _groupBroken = true;
        }
    }

    private SessionEditResult AfterChange(SessionEditResult result, bool structural, IReadOnlyList<string> warnings,
        IReadOnlyList<ValueChange>? semanticChanges = null, bool effectCoverage = false)
    {
        if (result.Ok) _semantic.Observe(semanticChanges ?? result.Changes, structural,
            id => _index.Spec(id) is not null, effectCoverage);
        if (!structural && result.Ok && result.Changes.Count > 0)
        {
            _revision++;
        }

        var rebuildWarnings = structural ? RebuildIndex() : [];
        return result with
        {
            StructureChanged = result.StructureChanged || structural,
            Warnings = [.. result.Warnings, .. warnings, .. rebuildWarnings],
            State = StateUnlocked(),
        };
    }

    private SessionEditResult Step(HistoryEntry entry, bool undo)
    {
        var run = HistoryReplay.Run(entry.Steps, undo, ApplyStep);
        if (!run.Outcome.Succeeded)
        {
            IReadOnlyList<string> notice = [];
            if (run.RollbackFailed)
            {
                // The document is somewhere between both states; history no longer describes it.
                _history.Clear(irreversible: true);
                notice = ["The step failed part-way and could not be fully reverted; undo history was cleared."];
            }

            return AfterChange(FromOutcome(run.Outcome), run.NeedsRebuild, notice);
        }

        var inverse = new HistoryEntry(run.Swapped);
        var warnings = undo ? _history.CompleteUndo(inverse) : _history.CompleteRedo(inverse);
        return AfterChange(FromOutcome(run.Outcome), run.NeedsRebuild, warnings);
    }

    private HistoryReplay.StepResult ApplyStep(HistoryStep step, bool undo)
    {
        switch (step)
        {
            case ValueStep values:
                return new HistoryReplay.StepResult(Revert(values.Changes, useOld: undo), values, Rebuilt: false);
            case MementoStep memento:
                DocumentMemento? inverse = null;
                try
                {
                    inverse = Document.RestoreMemento(memento.Memento);
                    CheckSnapshotBudget(inverse);

                    // Later steps in the same entry may edit values of the restored records: re-bind them now.
                    var warnings = RebuildIndex();
                    return new HistoryReplay.StepResult(new EditOutcome { StructureChanged = true, Warnings = warnings }, new MementoStep(inverse), Rebuilt: true);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // A provider rejection before returning an inverse is the original replay
                    // failure contract. It is unrelated to the host's inverse-size admission.
                    if (inverse is null)
                        return new HistoryReplay.StepResult(EditOutcome.Rejected(string.Empty,
                            $"Plugin error: {ActionEffectOutcomes.Diagnostic(ex)}") with { StructureChanged = true },
                            memento, Rebuilt: false);
                    if (inverse is not null)
                    {
                        try
                        {
                            _ = Document.RestoreMemento(inverse);
                            var rebindWarnings = RebuildIndex(forceNewRevision: false);
                            if (rebindWarnings.Count > 0)
                                throw new InvalidOperationException(ActionEffectOutcomes.Bound(rebindWarnings[0]));
                        }
                        catch (Exception restore) when (restore is not OutOfMemoryException)
                        {
                            MarkUnusable();
                            return new HistoryReplay.StepResult(EditOutcome.Rejected(string.Empty,
                                $"History replay exceeded its snapshot budget and could not restore the document: {ActionEffectOutcomes.Diagnostic(restore)}"),
                                memento, Rebuilt: false);
                        }
                    }
                    // The document rolled itself back, but may have replaced model objects: re-index.
                    return new HistoryReplay.StepResult(EditOutcome.Rejected(string.Empty,
                        $"History replay rejected an oversized snapshot: {ActionEffectOutcomes.Diagnostic(ex)}") with { StructureChanged = true },
                        memento, Rebuilt: false);
                }

            default:
                throw new InvalidOperationException($"Unknown history step {step.GetType().Name}.");
        }
    }

    /// <summary>
    /// Re-indexes the current view after a structural change: re-expands lazy sections the client had
    /// opened (so their bindings stay live), retires ids that vanished and bumps the revision.
    /// </summary>
    private IReadOnlyList<string> RebuildIndex(bool forceNewRevision = true)
    {
        var fresh = new ViewIndex(Document);
        HashSet<string> expanded;
        try
        {
            var view = Document.BuildView();
            fresh.Add(view);
            _ = fresh.ResolveFieldReferences(Document, view);
            expanded = ReExpand(fresh, _expanded);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Keep the previous index rather than retiring live ids; clients refetch on the new revision.
            if (forceNewRevision)
            {
                _structuralRevision = ++_revision;
            }

            return [$"The view could not be rebuilt: {ActionEffectOutcomes.Diagnostic(ex)}"];
        }

        var before = _index.BindingIds.ToHashSet(StringComparer.Ordinal);
        var now = fresh.BindingIds.ToHashSet(StringComparer.Ordinal);
        _index = fresh;
        _expanded = expanded;
        if (forceNewRevision || !before.SetEquals(now))
        {
            _structuralRevision = ++_revision;
        }

        before.ExceptWith(now);
        if (_retired.Count + before.Count > MaxRetiredIds)
        {
            // Bounded memory; old ids then fall back to the plain "unknown" rejection.
            _retired.Clear();
        }

        _retired.UnionWith(before);
        _retired.ExceptWith(now);
        return [];
    }

    /// <summary>
    /// Expands every previously opened lazy section that exists in <paramref name="index"/> (nested ones
    /// included). Sections that vanished stay remembered, so they are re-expanded if an undo brings their
    /// record back; values in them may be replayed by later history steps.
    /// </summary>
    private HashSet<string> ReExpand(ViewIndex index, HashSet<string> lazyIds)
    {
        var result = LazyViewExpansion.Expand(Document, index, lazyIds);
        var expanded = result.Expanded;

        if (expanded.Count + result.Missing.Count <= MaxRetiredIds)
        {
            expanded.UnionWith(result.Missing);
        }

        return expanded;
    }
}
