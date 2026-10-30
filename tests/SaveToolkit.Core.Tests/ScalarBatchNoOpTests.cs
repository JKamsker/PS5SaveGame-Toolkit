using System.Collections;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class ScalarBatchNoOpTests
{
    private const int FieldCount = 10_000;

    [Fact]
    public void DistinctRepeatedBatchProofReadsRequestsWithinALinearBound()
    {
        using var document = new ScalarBatchDocument(FieldCount);
        document.BuildView();
        var requested = Enumerable.Range(0, FieldCount)
            .Select(index => new ValueEdit(ScalarBatchDocument.Id(index), FieldValue.Of((long)index))).ToArray();
        var before = ScalarEditEffectiveness.Capture(document, requested, CancellationToken.None);
        Assert.NotNull(before);
        var outcome = document.ApplyEdits(requested);
        Assert.Equal(FieldCount, outcome.Changes.Count);
        var counted = new CountedEdits(requested);
        var completed = ScalarEditEffectiveness.Complete(document, counted, before, outcome);
        Assert.Empty(completed.Changes);
        Assert.InRange(counted.Reads, 1, FieldCount * 2);
        Assert.Equal(FieldCount, document.Writes);
        Assert.Equal(FieldCount, document.Validations);
        AssertBytes(before, document);
    }

    [Fact]
    public void LargeRepeatedBatchPreservesAllBytesRevisionHistoryAndRedoAfterSetters()
    {
        using var document = new ScalarBatchDocument(FieldCount);
        using var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();
        var original = document.Serialize();
        var incoming = Enumerable.Range(0, FieldCount).Select(index => Incoming(index, index)).ToArray();
        var clean = fixture.Session.State;
        Assert.Empty(fixture.Session.ApplyEdits(incoming, clean.Revision).Changes);
        Assert.Equal(clean, fixture.Session.State);
        Assert.Equal(FieldCount, document.Writes);
        Assert.Equal(FieldCount, document.Validations);
        AssertBytes(original, document);

        Assert.Single(fixture.Session.ApplyEdits([Incoming(0, -1)]).Changes);
        incoming[0] = Incoming(0, -1);
        var dirty = fixture.Session.State;
        var edited = document.Serialize();
        Assert.Empty(fixture.Session.ApplyEdits(incoming, dirty.Revision).Changes);
        Assert.Equal(dirty, fixture.Session.State);
        AssertBytes(edited, document);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.False(fixture.Session.State.CanUndo);
        Assert.True(fixture.Session.State.CanRedo);
        AssertBytes(original, document);
        incoming[0] = Incoming(0, 0);
        var undone = fixture.Session.State;
        Assert.Empty(fixture.Session.ApplyEdits(incoming, undone.Revision).Changes);
        Assert.Equal(undone, fixture.Session.State);
        AssertBytes(original, document);
        Assert.True(fixture.Session.Redo().Ok);
        AssertBytes(edited, document);
    }

    [Fact]
    public void DuplicateRepeatedAssignmentsStillProveIneffective()
    {
        using var document = new ScalarBatchDocument(1);
        using var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();
        var original = document.Serialize();
        var result = fixture.Session.ApplyEdits([Incoming(0, 0), Incoming(0, 0)]);
        Assert.True(result.Ok);
        Assert.Empty(result.Changes);
        Assert.Equal(2, document.Writes);
        Assert.Equal(new DocumentState(false, false, false), result.State);
        AssertBytes(original, document);
    }

    public static TheoryData<string, FieldValue> UnrequestedRows => new()
    {
        { "SCALAR/0", FieldValue.Of(0L) },
        { "scalar/0", FieldValue.Of(0d) },
        { "scalar/0", FieldValue.Of(1L) },
    };

    [Theory]
    [MemberData(nameof(UnrequestedRows))]
    public void MembershipRetainsOrdinalIdsAndExactTypedValues(string id, FieldValue value)
    {
        using var document = new ScalarBatchDocument(1);
        document.BuildView();
        ValueEdit[] requested = [new("scalar/0", FieldValue.Of(0L))];
        var before = ScalarEditEffectiveness.Capture(document, requested, CancellationToken.None);
        Assert.NotNull(before);
        var outcome = new EditOutcome { Changes = [new(id, value, value)] };
        Assert.Same(outcome, ScalarEditEffectiveness.Complete(document, requested, before, outcome));
    }

    private static IncomingEdit Incoming(int index, long value) =>
        new(ScalarBatchDocument.Id(index), JsonSerializer.SerializeToElement(FieldValue.Of(value)));

    private static void AssertBytes(IReadOnlyList<FileOutput> expected, ISaveDocument document)
    {
        var actual = document.Serialize();
        Assert.Equal(expected.Select(output => output.RelativePath), actual.Select(output => output.RelativePath));
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
            Assert.Equal(expected[index].Content.ToArray(), actual[index].Content.ToArray());
    }

    private sealed class CountedEdits(IReadOnlyList<ValueEdit> edits) : IReadOnlyList<ValueEdit>
    {
        public int Reads { get; private set; }
        public int Count => edits.Count;
        public ValueEdit this[int index] => ++Reads <= FieldCount * 2 ? edits[index]
            : throw new InvalidOperationException("Repeated proof exceeded the linear request-read budget.");
        public IEnumerator<ValueEdit> GetEnumerator()
        {
            for (var index = 0; index < Count; index++) yield return this[index];
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
