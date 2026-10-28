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
        if (_groupBroken || steps.Count == 0)
        {
            return new SessionEditResult { State = StateUnlocked() };
        }

        var run = Replay(steps, undo: true);
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

    private SessionEditResult AfterChange(SessionEditResult result, bool structural, IReadOnlyList<string> warnings)
    {
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
        var run = Replay(entry.Steps, undo);
        if (!run.Outcome.Succeeded)
        {
            return AfterChange(FromOutcome(run.Outcome), run.NeedsRebuild, []);
        }

        var inverse = new HistoryEntry(run.Swapped);
        var warnings = undo ? _history.CompleteUndo(inverse) : _history.CompleteRedo(inverse);
        return AfterChange(FromOutcome(run.Outcome), run.NeedsRebuild, warnings);
    }

    /// <summary>
    /// Applies <paramref name="steps"/> backwards (undo) or forwards (redo). On failure, the steps
    /// already applied are reverted so the document is where it started.
    /// </summary>
    private ReplayRun Replay(IReadOnlyList<HistoryStep> steps, bool undo)
    {
        var swapped = new HistoryStep[steps.Count];
        var done = new List<int>(steps.Count);
        var changes = new List<ValueChange>();
        var warnings = new List<string>();
        var structural = false;
        var needsRebuild = false;
        void Track(StepResult r)
        {
            structural |= r.Outcome.StructureChanged;
            needsRebuild = !r.Rebuilt && (needsRebuild || r.Outcome.StructureChanged);
        }

        for (var n = 0; n < steps.Count; n++)
        {
            var i = undo ? steps.Count - 1 - n : n;
            var step = ApplyStep(steps[i], undo);
            Track(step);
            if (!step.Outcome.Succeeded)
            {
                for (var k = done.Count - 1; k >= 0; k--)
                {
                    Track(ApplyStep(swapped[done[k]], !undo));
                }

                return new ReplayRun(step.Outcome with { StructureChanged = structural }, swapped, needsRebuild);
            }

            swapped[i] = step.Inverse;
            done.Add(i);
            changes.AddRange(step.Outcome.Changes);
            warnings.AddRange(step.Outcome.Warnings);
        }

        return new ReplayRun(new EditOutcome { Changes = changes, Warnings = warnings, StructureChanged = structural }, swapped, needsRebuild);
    }

    private StepResult ApplyStep(HistoryStep step, bool undo)
    {
        switch (step)
        {
            case ValueStep values:
                return new StepResult(Revert(values.Changes, useOld: undo), values, Rebuilt: false);
            case MementoStep memento:
                try
                {
                    var inverse = Document.RestoreMemento(memento.Memento);

                    // Later steps in the same entry may edit values of the restored records: re-bind them now.
                    var warnings = RebuildIndex();
                    return new StepResult(new EditOutcome { StructureChanged = true, Warnings = warnings }, new MementoStep(inverse), Rebuilt: true);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // The document rolled itself back, but may have replaced model objects: re-index.
                    return new StepResult(EditOutcome.Rejected(string.Empty, $"Plugin error: {ex.Message}") with { StructureChanged = true }, step, Rebuilt: false);
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
        var before = _index.BindingIds.ToHashSet(StringComparer.Ordinal);
        var wasExpanded = _expanded;
        _index.Clear();
        _expanded = new HashSet<string>(StringComparer.Ordinal);

        IReadOnlyList<string> warnings = [];
        try
        {
            _index.Add(Document.BuildView());
            ReExpand(wasExpanded);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            warnings = [$"The view could not be rebuilt: {ex.Message}"];
        }

        var now = _index.BindingIds.ToHashSet(StringComparer.Ordinal);
        if (forceNewRevision || !before.SetEquals(now))
        {
            _revision++;
        }

        before.ExceptWith(now);
        if (_retired.Count + before.Count > MaxRetiredIds)
        {
            // Bounded memory; old ids then fall back to the plain "unknown" rejection.
            _retired.Clear();
        }

        _retired.UnionWith(before);
        _retired.ExceptWith(now);
        return warnings;
    }

    private void ReExpand(HashSet<string> lazyIds)
    {
        var pending = new HashSet<string>(lazyIds, StringComparer.Ordinal);
        var progress = true;
        while (progress && pending.Count > 0)
        {
            progress = false;
            foreach (var lazyId in pending.Where(_index.KnowsLazy).ToList())
            {
                _index.AddAll(Document.ExpandNode(lazyId));
                _expanded.Add(lazyId);
                pending.Remove(lazyId);
                progress = true;
            }
        }
    }

    private sealed record StepResult(EditOutcome Outcome, HistoryStep Inverse, bool Rebuilt);

    /// <param name="NeedsRebuild">A structural change happened after the last index rebuild.</param>
    private sealed record ReplayRun(EditOutcome Outcome, IReadOnlyList<HistoryStep> Swapped, bool NeedsRebuild);
}
