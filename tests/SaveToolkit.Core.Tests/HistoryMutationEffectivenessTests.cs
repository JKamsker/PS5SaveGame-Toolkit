using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class HistoryMutationEffectivenessTests
{
    [Fact]
    public void UndoTrimmingKeepsTheNextEntryWhenTheBudgetIsExactlyReached()
    {
        var history = new EditHistory(new() { MaxSnapshotBytes = 7 });
        history.Record(Entry(2));
        history.Record(Entry(1));
        history.Record(Entry(1));
        history.Record(Entry(1));
        history.CompleteUndo(Entry(2));
        Assert.NotEmpty(history.CompleteUndo(Entry(4)));
        Assert.True(history.CanUndo);
        Assert.Equal(1, history.NextUndo!.SizeBytes);
    }

    [Fact]
    public void RedoTrimmingStopsAtTheExactBudgetAfterUndoEntriesAreExhausted()
    {
        var history = new EditHistory(new() { MaxSnapshotBytes = 7 });
        history.Record(Entry(1));
        history.Record(Entry(1));
        history.Record(Entry(1));
        history.CompleteUndo(Entry(3));
        Assert.NotEmpty(history.CompleteUndo(Entry(4)));
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
        Assert.Empty(history.CompleteRedo(Entry(4)));
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void FailedStepCompensationMustTrackItsOwnStructuralRebuild()
    {
        HistoryStep[] steps = [Snapshot(1), Snapshot(2)];
        var result = HistoryReplay.Run(steps, undo: false, (step, undo) =>
            new(!undo && step.SizeBytes == 2 ? EditOutcome.Rejected("value", "Rejected")
                : new EditOutcome { StructureChanged = undo }, step, Rebuilt: false));
        Assert.False(result.Outcome.Succeeded);
        Assert.True(result.Outcome.StructureChanged);
        Assert.True(result.NeedsRebuild);
    }

    [Fact]
    public void DefaultHistoryBudgetCanRetainAMegabyteSnapshot()
    {
        const int snapshotBytes = 1024 * 1024;
        var history = new EditHistory(new DocumentSessionOptions());
        Assert.Empty(history.Record(Entry(snapshotBytes)));
        Assert.True(history.CanUndo);
    }

    [Fact]
    public void GroupedSnapshotCostIsTheSumAndTheBudgetIsInclusive()
    {
        var entry = new HistoryEntry([Snapshot(3), Snapshot(4)]);
        Assert.Equal(7, entry.SizeBytes);
        var history = new EditHistory(new() { MaxSnapshotBytes = 7 });

        Assert.Empty(history.Record(entry));
        Assert.True(history.CanUndo);
        Assert.False(history.IrreversiblyChanged);
        Assert.Same(entry, history.NextUndo);
    }

    [Fact]
    public void UndoRedoAndNewEditsAccountForDifferentSizedTokensAndClearRedo()
    {
        var history = new EditHistory(new() { MaxSnapshotBytes = 7 });
        var first = Entry(3);
        var second = Entry(4);
        Assert.Empty(history.Record(first));
        Assert.Empty(history.Record(second));

        Assert.Empty(history.CompleteUndo(Entry(4)));
        Assert.True(history.CanUndo);
        Assert.True(history.CanRedo);
        Assert.Same(first, history.NextUndo);
        Assert.Empty(history.CompleteRedo(second));
        Assert.False(history.CanRedo);
        Assert.Same(second, history.NextUndo);

        Assert.Empty(history.CompleteUndo(Entry(4)));
        Assert.Empty(history.Record(Entry(4)));
        Assert.False(history.CanRedo);
        Assert.False(history.IrreversiblyChanged);
        Assert.Empty(history.CompleteUndo(Entry(4)));
        Assert.Empty(history.CompleteUndo(Entry(3)));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void HistoryCountEvictionPermanentlyMarksTheDocumentDirty()
    {
        var history = new EditHistory(new() { MaxHistory = 1 });
        history.Record(Entry(1));
        var retained = Entry(2);
        history.Record(retained);
        Assert.True(history.IrreversiblyChanged);
        Assert.Same(retained, history.NextUndo);

        history.Clear(irreversible: true);
        Assert.True(history.IrreversiblyChanged);
        history.Clear(irreversible: true);
        Assert.True(history.IrreversiblyChanged);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void EnlargedInverseTokensEvictUndoThenRedoToStayWithinBudget()
    {
        var history = new EditHistory(new() { MaxSnapshotBytes = 7 });
        history.Record(Entry(3));
        history.Record(Entry(4));

        Assert.Single(history.CompleteUndo(Entry(6)));
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
        Assert.True(history.IrreversiblyChanged);
        Assert.Empty(history.CompleteRedo(Entry(6)));
        Assert.Single(history.CompleteUndo(Entry(8)));
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void ScalarReplayDoesNotInventStructuralChangesOrRollbackFailures()
    {
        HistoryStep[] steps = [new ValueStep([])];
        var result = HistoryReplay.Run(steps, undo: true,
            (step, _) => new(new EditOutcome(), step, Rebuilt: false));
        Assert.True(result.Outcome.Succeeded);
        Assert.False(result.Outcome.StructureChanged);
        Assert.False(result.NeedsRebuild);
        Assert.False(result.RollbackFailed);
    }

    [Fact]
    public void RepeatedStructuralStepsRetainTheirRebuildRequirement()
    {
        HistoryStep[] steps = [Snapshot(1), Snapshot(2)];
        var result = HistoryReplay.Run(steps, undo: false,
            (step, _) => new(new EditOutcome { StructureChanged = true }, step, Rebuilt: false));
        Assert.True(result.Outcome.StructureChanged);
        Assert.True(result.NeedsRebuild);
        Assert.False(result.RollbackFailed);
    }

    [Fact]
    public void EveryCompletedStepIsCompensatedEvenWhenTwoCompensationsFail()
    {
        HistoryStep[] steps = [Snapshot(1), Snapshot(2), Snapshot(3)];
        var calls = new List<long>();
        var result = HistoryReplay.Run(steps, undo: false, (step, undo) =>
        {
            calls.Add(undo ? -step.SizeBytes : step.SizeBytes);
            var outcome = undo || step.SizeBytes == 3 ? EditOutcome.Rejected("value", "Rejected") : new EditOutcome();
            return new(outcome, step, Rebuilt: false);
        });
        Assert.False(result.Outcome.Succeeded);
        Assert.True(result.RollbackFailed);
        Assert.Equal([1L, 2L, 3L, -2L, -1L], calls);
    }

    [Fact]
    public void SemanticByteCounterIncludesPreviousWritesAndAcceptsTheExactBudget()
    {
        using var stream = new SemanticQueryByteCounter(3);
        Assert.False(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.True(stream.CanWrite);
        stream.Write([1, 2]);
        stream.WriteByte(3);
        Assert.Equal(3, stream.BytesWritten);
        Assert.Throws<SemanticQueryByteLimitException>(() => stream.WriteByte(4));
        Assert.Equal(3, stream.Length);
    }

    private static MementoStep Snapshot(long size) => new(DocumentMemento.Snapshot("state", size));
    private static HistoryEntry Entry(long size) => new([Snapshot(size)]);
}
