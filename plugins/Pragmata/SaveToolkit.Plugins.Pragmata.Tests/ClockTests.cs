using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class ClockTests
{
    [Fact]
    public void EditingPlaytimeAdjustsOnlyElapsedAndReportsDerivedChangeForUndo()
    {
        using var document = EditingTests.Gameplay();
        var original = document.Serialize().Single().Content.ToArray();
        var playtime = document.Layout.Header!.Find(KnownHashes.Playtime)!;
        var oldElapsed = document.Read(document.Layout.Timers[0]).AsInteger();
        var oldDemo = document.Read(document.Layout.Timers[1]);
        var oldPause = document.Read(document.Layout.Timers[2]);
        var outcome = document.ApplyEdits([new(playtime.Path, FieldValue.Of(document.Read(playtime).AsInteger() + 1_000_000))]);
        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.Changes.Count);
        Assert.Contains(outcome.Changes, change => change.ValueId == document.Layout.Timers[0].Path && change.NewValue.AsInteger() == oldElapsed + 1_000_000);
        Assert.Equal(oldDemo, document.Read(document.Layout.Timers[1]));
        Assert.Equal(oldPause, document.Read(document.Layout.Timers[2]));
        using var reopened = new PragmataDocument("data000.bin", document.Serialize().Single().Content.Span);
        AssertRelation(reopened);
        Assert.True(document.ApplyEdits(outcome.Changes.Reverse().Select(c => new ValueEdit(c.ValueId, c.OldValue)).ToArray()).Succeeded);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EditingEachClockRecomputesPlaytimeAndSurvivesReopen(int timerIndex)
    {
        using var document = EditingTests.Gameplay();
        var timer = document.Layout.Timers[timerIndex];
        var playtime = document.Layout.Header!.Find(KnownHashes.Playtime)!;
        var oldPlaytime = document.Read(playtime).AsInteger();
        var outcome = document.ApplyEdits([new(timer.Path, FieldValue.Of(document.Read(timer).AsInteger() + 1))]);
        Assert.True(outcome.Succeeded);
        Assert.Equal(oldPlaytime + (timerIndex == 0 ? 1 : -1), document.Read(playtime).AsInteger());
        Assert.Equal(2, outcome.Changes.Count);
        using var reopened = new PragmataDocument("data000.bin", document.Serialize().Single().Content.Span);
        AssertRelation(reopened);
    }

    [Fact]
    public void ConflictingOverflowingAndUnderflowingClockBatchesAreAtomic()
    {
        using var document = EditingTests.Gameplay();
        var original = document.Serialize().Single().Content.ToArray();
        var playtime = document.Layout.Header!.Find(KnownHashes.Playtime)!;
        var elapsed = document.Layout.Timers[0];
        ValueEdit[][] batches = [
            [new(playtime.Path, FieldValue.Of(ulong.MaxValue))],
            [new(elapsed.Path, FieldValue.Of(0UL))],
            [new(playtime.Path, FieldValue.Of(1UL)), new(elapsed.Path, document.Read(elapsed))],
            [new(document.Layout.Timers[1].Path, FieldValue.Of(ulong.MaxValue)), new(document.Layout.Timers[2].Path, FieldValue.Of(ulong.MaxValue))],
        ];
        foreach (var batch in batches) Assert.False(document.ApplyEdits(batch).Succeeded);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void PlaytimeAndPauseBatchDerivesElapsedWithoutLosingRequestedValues()
    {
        using var document = EditingTests.Gameplay();
        var playtime = document.Layout.Header!.Find(KnownHashes.Playtime)!;
        var pause = document.Layout.Timers[2];
        Assert.True(document.ApplyEdits([new(playtime.Path, FieldValue.Of(0UL)), new(pause.Path, FieldValue.Of(123UL))]).Succeeded);
        Assert.Equal(0UL, document.Read(playtime).AsUInt64());
        Assert.Equal(123UL, document.Read(pause).AsUInt64());
        AssertRelation(document);
    }

    private static void AssertRelation(PragmataDocument document) => Assert.Equal(
        document.Read(document.Layout.Header!.Find(KnownHashes.Playtime)!).AsInteger(),
        document.Read(document.Layout.Timers[0]).AsInteger() - document.Read(document.Layout.Timers[1]).AsInteger() - document.Read(document.Layout.Timers[2]).AsInteger());
}
