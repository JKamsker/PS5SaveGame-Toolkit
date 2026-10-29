using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class CaptionMementoTests
{
    [Fact]
    public void WholeByteUndoRestoresNonzeroReservedSuffixAndRetainsGameplayState()
    {
        var save = new SyntheticSave(false);
        var sfo = CaptionTestData.Sfo();
        using var document = SuckerPunchTestData.Open(CaptionTestData.File(sfo, save));
        Assert.True(document.ApplyEdits([new(save.ResourceId, FieldValue.Of(9L))]).Succeeded);
        var staged = CaptionTestData.Primary(document);
        var outcome = CaptionTestData.Apply(document, "Longer 😀 caption");
        Assert.True(outcome.Succeeded);
        Assert.Single(outcome.Changes);
        Assert.NotNull(outcome.Revert);
        Assert.InRange(outcome.Revert.SizeBytes, save.Bytes.LongLength + sfo.Length, save.Bytes.LongLength + sfo.Length + 1024);
        var edited = CaptionTestData.Metadata(document);
        Assert.NotEqual(sfo, edited);
        Assert.Equal(staged, CaptionTestData.Primary(document));
        WriteAssertions.OnlyRangesChanged(sfo, edited, [(24, 4), (CaptionTestData.DataStart, 20)]);
        // Replaying only the original short text demonstrably loses the original reserved bytes.
        var patcher = SaveToolkit.Sdk.Saves.ParamSfoDocument.Parse(edited);
        Assert.NotEqual(sfo, patcher.Apply(patcher.PlanExistingUtf8("SUBTITLE", CaptionTestData.Original)).ToArray());
        var redo = document.RestoreMemento(outcome.Revert);
        _ = document.BuildView();
        Assert.Equal(sfo, CaptionTestData.Metadata(document));
        Assert.Equal(staged, CaptionTestData.Primary(document));
        Assert.Equal(CaptionTestData.Original, document.GetValue(CaptionTestData.ValueId).AsString());
        _ = document.RestoreMemento(redo);
        _ = document.BuildView();
        Assert.Equal(edited, CaptionTestData.Metadata(document));
        Assert.Equal(2, document.Serialize().Count);
        Assert.Equal(9U, document.GetValue(save.ResourceId).AsUInt32());
    }

    [Fact]
    public void MementoCannotRestoreAnotherDocumentEvenWithSamePaths()
    {
        using var first = SuckerPunchTestData.Open(CaptionTestData.File(CaptionTestData.Sfo()));
        using var second = SuckerPunchTestData.Open(CaptionTestData.File(CaptionTestData.Sfo()));
        var token = CaptionTestData.Apply(first, "first").Revert!;
        Assert.Throws<InvalidOperationException>(() => second.RestoreMemento(token));
        Assert.Equal(CaptionTestData.Sfo(), CaptionTestData.Metadata(second));
        Assert.Equal(CaptionTestData.Original, second.GetValue(CaptionTestData.ValueId).AsString());
    }

    [Fact]
    public void NoOpHasNoMementoAndDirectEditsCannotSplitMixedGameplayCaptionBatch()
    {
        var sfo = CaptionTestData.Sfo();
        using var document = SuckerPunchTestData.Open(CaptionTestData.File(sfo));
        var plan = CaptionTestData.Plan(document, CaptionTestData.Original);
        Assert.Empty(plan.Changes);
        var noOp = CaptionTestData.Apply(document, CaptionTestData.Original);
        Assert.True(noOp.Succeeded);
        Assert.Empty(noOp.Changes);
        Assert.Null(noOp.Revert);
        Assert.False(noOp.StructureChanged);
        Assert.Equal(2, document.Serialize().Count);
        Assert.False(document.ApplyEdits([new(CaptionTestData.ValueId, FieldValue.Of("new")),
            new(SuckerPunchTestData.SuppliesId, FieldValue.Of(3L))]).Succeeded);
        Assert.Equal(sfo, CaptionTestData.Metadata(document));
    }
}
