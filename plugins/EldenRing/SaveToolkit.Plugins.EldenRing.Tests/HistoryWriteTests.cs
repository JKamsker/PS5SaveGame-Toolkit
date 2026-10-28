using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class HistoryWriteTests
{
    private const string HistoryId = "slots/0/rune-memory";
    private const string LinkedId = "slots/0/runes/linked";

    [Fact]
    public void SampleHistory_PersistsBothCopiesAndUndoRedoRestoresExactBytes()
    {
        var file = WriteTestSupport.Sample();
        var original = file.ReadAllBytes();
        var slot = EldenRingParser.Parse(original).Slots[0];
        using var session = WriteTestSupport.Session(file, out var document);
        _ = session.Expand("characters/0");
        Assert.True(session.ApplyEdits([WriteTestSupport.Incoming(HistoryId, 4426)]).Ok);
        var expected = original.ToArray();
        var offsets = HistoryOffsets(slot);
        foreach (var offset in offsets) BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(offset), 4426);
        WriteTestSupport.EqualBytes(expected, document);
        WriteTestSupport.OnlyRanges(original, expected, offsets.Select(offset => (offset, sizeof(uint))).ToArray());
        using var reopened = WriteTestSupport.Reopen(document);
        _ = reopened.ExpandNode("characters/0");
        Assert.Equal(4426U, reopened.GetValue(HistoryId).AsUInt32());
        WriteTestSupport.EqualBytes(expected, document); // Serialization never increments history.
        Assert.True(session.Undo().Ok);
        WriteTestSupport.EqualBytes(original, document);
        Assert.True(session.Redo().Ok);
        WriteTestSupport.EqualBytes(expected, document);
    }

    [Fact]
    public void SampleDerivedLinkedIncrease_PreviewsBothEffectsAndAppliesAsOneUndoUnit()
    {
        var file = WriteTestSupport.Sample();
        var original = file.ReadAllBytes();
        var slot = EldenRingParser.Parse(original).Slots[0];
        // Sample-derived lower history gives a real linked transition without inventing large rune values.
        foreach (var offset in HistoryOffsets(slot)) BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(offset), 1147);
        using var session = WriteTestSupport.Session(file, out var document, original);
        _ = session.Expand("characters/0");
        var incoming = new Dictionary<string, JsonElement> { ["runes"] = JsonSerializer.SerializeToElement(1148) };
        var preview = session.PlanAction(LinkedId, incoming);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(2, preview.Plan.Changes.Count);
        Assert.True(preview.Plan.Changes.Single(change => change.ValueId == HistoryId).Derived);
        Assert.NotEmpty(preview.Plan.Warnings);
        WriteTestSupport.EqualBytes(original, document);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        var expected = original.ToArray();
        var offsets = HistoryOffsets(slot).Append(slot.PlayerOffset!.Value + EldenRingLayout.RunesOffset).ToArray();
        foreach (var offset in offsets) BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(offset), 1148);
        WriteTestSupport.EqualBytes(expected, document);
        WriteTestSupport.OnlyRanges(original, expected, offsets.Select(offset => (offset, sizeof(uint))).ToArray());
        using var reopened = WriteTestSupport.Reopen(document);
        _ = reopened.ExpandNode("characters/0");
        Assert.Equal(1148U, reopened.GetValue(HistoryId).AsUInt32());
        Assert.Equal(1148U, reopened.GetValue("slots/0/runes").AsUInt32());
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        WriteTestSupport.EqualBytes(original, document);
        Assert.True(session.Redo().Ok);
        WriteTestSupport.EqualBytes(expected, document);
        var noop = session.PlanAction(LinkedId, incoming);
        Assert.Empty(noop.Plan.Changes);
        Assert.True(session.ApplyActionPlan(noop.PlanId!, noop.Revision).Ok);
        WriteTestSupport.EqualBytes(expected, document);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(4428L)]
    [InlineData(long.MaxValue)]
    public void InvalidHistory_RejectsWholeBatchIncludingCandidateInvariantFailures(long invalid)
    {
        var fixture = new SyntheticSave([0]);
        using var document = fixture.Open();
        _ = document.ExpandNode("characters/0");
        Assert.False(document.ApplyEdits([new("slots/0/name", FieldValue.Of("Changed")), new(HistoryId, FieldValue.Of(invalid))]).Succeeded);
        Assert.False(document.ApplyEdits([new(HistoryId, FieldValue.Of("1001"))]).Succeeded);
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
    }

    [Fact]
    public void LinkedPlanning_RejectsTamperingBoundsScopesAndReadOnlyVersions()
    {
        var fixture = new SyntheticSave([0, 9]);
        using var document = fixture.Open();
        var planner = (IActionPlanningDocument)document;
        var parameters = new Dictionary<string, FieldValue> { ["runes"] = FieldValue.Of(1001L) };
        var plan = planner.PlanAction(LinkedId, parameters, new());
        Assert.Equal(2, plan.Changes.Count);
        Assert.Throws<InvalidOperationException>(() => document.InvokeAction(LinkedId, parameters));
        Assert.False(planner.ApplyActionPlan(LinkedId, parameters, new(), plan with { Warnings = [] }).Succeeded);
        Assert.False(planner.ApplyActionPlan(LinkedId, parameters, new(), plan with { Changes = [] }).Succeeded);
        Assert.NotEmpty(planner.PlanAction(LinkedId, parameters, new([])).Errors);
        Assert.NotEmpty(planner.PlanAction(LinkedId, new Dictionary<string, FieldValue> { ["runes"] = FieldValue.Of(4428L) }, new()).Errors);
        Assert.NotEmpty(planner.PlanAction(LinkedId, new Dictionary<string, FieldValue>(), new()).Errors);
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
        var applied = planner.ApplyActionPlan(LinkedId, parameters, new(), plan);
        Assert.True(applied.Succeeded); // Registers unopened character bindings.
        Assert.False(planner.ApplyActionPlan(LinkedId, parameters, new(), plan).Succeeded);
        Assert.True(document.ApplyEdits(applied.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
        using var readOnly = new SyntheticSave([0], version: 151).Open();
        Assert.NotEmpty(((IActionPlanningDocument)readOnly).PlanAction(LinkedId, parameters, new()).Errors);
    }

    private static int[] HistoryOffsets(CharacterSlot slot) =>
        [slot.PlayerOffset!.Value + EldenRingLayout.RuneMemoryOffset, slot.ProfileOffset + EldenRingLayout.ProfileRuneMemoryOffset];
}
