using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>
/// Replays an entry backwards for undo or forwards for redo, collecting inverse tokens in entry order.
/// On failure, compensates completed steps in reverse execution order. The caller owns locking,
/// plugin calls, view rebuilding and the decision to retain or clear history.
/// </summary>
internal static class HistoryReplay
{
    public static Result Run(IReadOnlyList<HistoryStep> steps, bool undo, Func<HistoryStep, bool, StepResult> applyStep)
    {
        var swapped = new HistoryStep[steps.Count];
        var done = new List<int>(steps.Count);
        var changes = new List<ValueChange>();
        var warnings = new List<string>();
        var structural = false;
        var needsRebuild = false;
        void Track(StepResult result)
        {
            structural |= result.Outcome.StructureChanged;
            needsRebuild = !result.Rebuilt && (needsRebuild || result.Outcome.StructureChanged);
        }

        for (var n = 0; n < steps.Count; n++)
        {
            var i = undo ? steps.Count - 1 - n : n;
            var step = applyStep(steps[i], undo);
            Track(step);
            if (!step.Outcome.Succeeded)
            {
                var rollbackFailed = false;
                for (var k = done.Count - 1; k >= 0; k--)
                {
                    var back = applyStep(swapped[done[k]], !undo);
                    Track(back);
                    rollbackFailed |= !back.Outcome.Succeeded;
                }

                return new Result(step.Outcome with { StructureChanged = structural }, swapped, needsRebuild, rollbackFailed);
            }

            swapped[i] = step.Inverse;
            done.Add(i);
            changes.AddRange(step.Outcome.Changes);
            warnings.AddRange(step.Outcome.Warnings);
        }

        return new Result(new EditOutcome { Changes = changes, Warnings = warnings, StructureChanged = structural }, swapped, needsRebuild, RollbackFailed: false);
    }

    internal sealed record StepResult(EditOutcome Outcome, HistoryStep Inverse, bool Rebuilt);

    /// <param name="NeedsRebuild">A structural change happened after the last index rebuild.</param>
    /// <param name="RollbackFailed">A step failed and the steps before it could not all be reverted.</param>
    internal sealed record Result(EditOutcome Outcome, IReadOnlyList<HistoryStep> Swapped, bool NeedsRebuild, bool RollbackFailed);
}
