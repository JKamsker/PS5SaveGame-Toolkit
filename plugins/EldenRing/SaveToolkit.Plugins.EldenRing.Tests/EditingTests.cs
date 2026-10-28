using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class EditingTests
{
    [Theory]
    [InlineData(0U)]
    [InlineData(SyntheticSave.LifetimeRunes)]
    public void RuneBounds_RoundTripWithoutChangingAnyOtherByte(uint runes)
    {
        const int tailLength = 17;
        var fixture = new SyntheticSave([0, 9], trailingBytes: tailLength);
        fixture.Bytes.AsSpan(SyntheticSave.FileSize, tailLength).Fill(0xA5);
        fixture.Bytes[SyntheticSave.Slot(5) + SyntheticSave.SlotSize - 1] = 0x5A;
        var expected = fixture.Bytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(fixture.Players[9] + SyntheticSave.Runes), runes);
        using var document = fixture.Open();
        _ = document.ExpandNode("characters/9");
        var outcome = document.ApplyEdits([new ValueEdit("slots/9/runes", FieldValue.Of((long)runes))]);
        Assert.True(outcome.Succeeded);
        var change = Assert.Single(outcome.Changes);
        Assert.Equal(SyntheticSave.InitialRunes, change.OldValue.AsUInt32());
        Assert.Equal(runes, change.NewValue.AsUInt32());
        var output = Assert.Single(document.Serialize());
        Assert.Equal(fixture.File().RelativePath, output.RelativePath);
        Assert.True(expected.AsSpan().SequenceEqual(output.Content.Span));

        using var reopened = new EldenRingPlugin().Open(MemorySaveSet.SingleFile("memory.dat", output.Content.ToArray(), "PPSA03169", "sce_sdmemory"));
        _ = reopened.ExpandNode("characters/9");
        Assert.Equal(runes, reopened.GetValue("slots/9/runes").AsUInt32());
        Assert.Equal(SyntheticSave.LifetimeRunes, reopened.GetValue("slots/9/rune-memory").AsUInt32());
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(SyntheticSave.LifetimeRunes + 1L)]
    [InlineData(long.MaxValue)]
    public void InvalidRuneEdit_RejectsTheWholeMultiSlotBatch(long invalid)
    {
        var fixture = new SyntheticSave([0, 9]);
        using var document = fixture.Open();
        _ = document.ExpandNode("characters/0");
        _ = document.ExpandNode("characters/9");
        var outcome = document.ApplyEdits(
        [
            new ValueEdit("slots/0/runes", FieldValue.Of(101L)),
            new ValueEdit("slots/9/runes", FieldValue.Of(invalid)),
        ]);
        Assert.False(outcome.Succeeded);
        Assert.Empty(outcome.Changes);
        Assert.Equal(SyntheticSave.InitialRunes, document.GetValue("slots/0/runes").AsUInt32());
        Assert.Equal(SyntheticSave.InitialRunes, document.GetValue("slots/9/runes").AsUInt32());
        Assert.True(fixture.Bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Theory]
    [InlineData("slots/0/level")]
    [InlineData("slots/0/attributes/vigor")]
    [InlineData("slots/0/equipment/ChrAsm2/0")]
    [InlineData("slots/0/unknown")]
    public void DependentAndUnknownFields_CannotBeEdited(string id)
    {
        var fixture = new SyntheticSave([0]);
        using var document = fixture.Open();
        _ = document.ExpandNode("characters/0");
        var outcome = document.ApplyEdits(
        [
            new ValueEdit("slots/0/runes", FieldValue.Of(101L)),
            new ValueEdit(id, FieldValue.Of(12L)),
        ]);
        Assert.False(outcome.Succeeded);
        Assert.True(fixture.Bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Fact]
    public void RuneField_RejectsWrongValueKindAndRemainsUndoable()
    {
        var fixture = new SyntheticSave([0]);
        using var document = fixture.Open();
        _ = document.ExpandNode("characters/0");
        Assert.False(document.ApplyEdits([new ValueEdit("slots/0/runes", FieldValue.Of("101"))]).Succeeded);
        var applied = document.ApplyEdits([new ValueEdit("slots/0/runes", FieldValue.Of(101L))]);
        Assert.True(applied.Succeeded);
        Assert.True(document.ApplyEdits(applied.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
        Assert.True(fixture.Bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Fact]
    public void RuneCap_DoesNotGrowBeyondGameLimitWhenLifetimeTotalIsLarger()
    {
        const uint gameCap = 999_999_999;
        var fixture = new SyntheticSave([0]);
        fixture.Write32(fixture.Players[0] + SyntheticSave.RuneMemory, uint.MaxValue);
        fixture.Write32(fixture.Profile(0) + SyntheticSave.ProfileMemory, uint.MaxValue);
        using var document = fixture.Open();
        var runes = document.ExpandNode("characters/0").OfType<FieldNode>().Single(field => field.ValueId == "slots/0/runes");
        Assert.Equal((Int128)gameCap, Assert.IsType<IntegerSpec>(runes.Spec).Max);
        Assert.False(document.ApplyEdits([new ValueEdit(runes.ValueId, FieldValue.Of((long)gameCap + 1))]).Succeeded);
        Assert.True(document.ApplyEdits([new ValueEdit(runes.ValueId, FieldValue.Of((long)gameCap))]).Succeeded);
        _ = document.Serialize();
    }

    [Fact]
    public void DocumentsAndSerializedOutputs_DoNotShareMutableState()
    {
        var fixture = new SyntheticSave([0]);
        using var first = fixture.Open();
        using var second = fixture.Open();
        _ = first.ExpandNode("characters/0");
        _ = second.ExpandNode("characters/0");
        var before = Assert.Single(first.Serialize()).Content;
        Assert.True(first.ApplyEdits([new ValueEdit("slots/0/runes", FieldValue.Of(101L))]).Succeeded);
        Assert.True(before.Span.SequenceEqual(fixture.Bytes));
        Assert.Equal(SyntheticSave.InitialRunes, second.GetValue("slots/0/runes").AsUInt32());
    }
}
