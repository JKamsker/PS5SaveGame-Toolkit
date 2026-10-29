using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class ScalarNoOpTests
{
    [Fact]
    public void RepeatedScalarStillRunsSetterAndPreservesWarningsWithoutHistory()
    {
        using var document = new ScalarEffectDocument(FieldValue.Of(17L), IntegerSpec.Int64);
        using var fixture = Open(document);
        var before = Bytes(document);
        var result = fixture.Session.ApplyEdits([Edit(FieldValue.Of(17L))]);
        Assert.True(result.Ok);
        Assert.Equal(1, document.Writes);
        Assert.Empty(result.Changes);
        Assert.Equal(new DocumentState(false, false, false), result.State);
        Assert.Equal(["Provider notice"], result.Warnings);
        AssertBytes(before, document);
    }

    [Fact]
    public void RepeatedInvalidOrReadOnlyValueIsStillRejected()
    {
        using var document = new ScalarEffectDocument(FieldValue.Of(17L), IntegerSpec.Int64) { Reject = true };
        using var fixture = Open(document);
        foreach (var id in new[] { "value", "readonly" })
        {
            var result = fixture.Session.ApplyEdits([Edit(FieldValue.Of(17L), id)]);
            Assert.False(result.Ok);
            Assert.Single(result.Errors);
            Assert.Equal(0, document.Writes);
            Assert.Equal(0, result.State.Revision);
        }
        Assert.Equal(17L, document.GetValue("value").AsInt64());
    }

    [Fact]
    public void MixedBatchKeepsEveryReportedRowAndOneUndoEntry()
    {
        using var document = new ScalarEffectDocument(FieldValue.Of(17L), IntegerSpec.Int64);
        using var fixture = Open(document);
        var before = Bytes(document);
        var result = fixture.Session.ApplyEdits([Edit(FieldValue.Of(17L)), Edit(FieldValue.Of(18L))]);
        Assert.Equal(2, result.Changes.Count);
        Assert.Equal(1, result.State.Revision);
        Assert.Equal(18L, document.GetValue("value").AsInt64());
        Assert.True(fixture.Session.Undo().Ok);
        Assert.False(fixture.Session.State.CanUndo);
        AssertBytes(before, document);
    }

    [Fact]
    public void RepeatedPrimaryRetainsDerivedEffects()
    {
        using var document = new ScalarEffectDocument(FieldValue.Of(17L), IntegerSpec.Int64) { Derived = true };
        using var fixture = Open(document);
        var result = fixture.Session.ApplyEdits([Edit(FieldValue.Of(17L))]);
        Assert.Equal(2, result.Changes.Count);
        Assert.Equal(FieldValue.Of(1L), document.GetValue("derived"));
        Assert.Equal(1L, BitConverter.ToInt64(document.Serialize()[2].Content.Span));
        Assert.True(result.State.CanUndo);
        Assert.Equal(1, result.State.Revision);
    }

    [Fact]
    public void UnreportedBytesAreNotMisclassifiedAsAnIneffectiveAssignment()
    {
        using var document = new ScalarEffectDocument(FieldValue.Of(17L), IntegerSpec.Int64) { UnreportedBytes = true };
        using var fixture = Open(document);
        var before = Bytes(document);
        var result = fixture.Session.ApplyEdits([Edit(FieldValue.Of(17L))]);
        Assert.Single(result.Changes);
        Assert.True(result.State.IsDirty);
        Assert.Equal(1, result.State.Revision);
        Assert.NotEqual(before[1].Content.ToArray(), document.Serialize()[1].Content.ToArray());
        Assert.Equal((byte)1, fixture.Session.Serialize()[1].Content.Span[0]);
        // A violating provider is retained, not repaired or given a false clean/undo guarantee.
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StructuralOrMementoOutcomeIsNeverSuppressed(bool memento)
    {
        using var document = new ScalarEffectDocument(FieldValue.Of(17L), IntegerSpec.Int64)
        { StructuralEffect = !memento, Memento = memento };
        using var fixture = Open(document);
        var result = fixture.Session.ApplyEdits([Edit(FieldValue.Of(17L))]);
        Assert.Single(result.Changes);
        Assert.True(result.State.CanUndo);
        Assert.Equal(1, result.State.Revision);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnavailableOrOversizeProofConservativelyRetainsMutation(bool fails)
    {
        using var document = new ScalarEffectDocument(FieldValue.Of(17L), IntegerSpec.Int64)
        { FailSnapshot = fails, OversizeSnapshot = !fails };
        using var fixture = Open(document);
        var result = fixture.Session.ApplyEdits([Edit(FieldValue.Of(17L))]);
        Assert.True(result.Ok);
        Assert.Single(result.Changes);
        Assert.True(result.State.CanUndo);
    }

    [Fact]
    public void SignedZeroStorageDifferenceRetainsAChangeDespiteTypedEquality()
    {
        using var document = new ScalarEffectDocument(FieldValue.Of(0d), new FloatSpec());
        using var fixture = Open(document);
        var before = Bytes(document);
        var result = fixture.Session.ApplyEdits([new("value", JsonSerializer.SerializeToElement("-0"))]);
        Assert.Single(result.Changes);
        Assert.Equal(long.MinValue, BitConverter.ToInt64(Assert.Single(fixture.Session.Serialize(), output => output.RelativePath == "value.dat").Content.Span));
        Assert.True(fixture.Session.Undo().Ok);
        AssertBytes(before, document);
    }

    public static TheoryData<FieldValue, FieldSpec> TypedScalars => new()
    {
        { FieldValue.Of((Int128)ulong.MaxValue), IntegerSpec.UInt64 },
        { FieldValue.ExactNumber("1.000000000000000000001"), new ExactNumberSpec() },
        { FieldValue.Null, new NullableSpec(new TextSpec()) },
        { FieldValue.Of("null"), new NullableSpec(new TextSpec()) },
        { FieldValue.Of(1.0000000000000002d), new FloatSpec() },
    };

    [Theory]
    [MemberData(nameof(TypedScalars))]
    public void TypedScalarProofDoesNotLosePrecisionOrNullDistinctions(FieldValue value, FieldSpec spec)
    {
        using var document = new ScalarEffectDocument(value, spec);
        using var fixture = Open(document);
        var before = Bytes(document);
        var result = fixture.Session.ApplyEdits([Edit(value)]);
        Assert.True(result.Ok);
        Assert.Empty(result.Changes);
        Assert.False(result.State.IsDirty);
        Assert.Equal(value, document.GetValue("value"));
        AssertBytes(before, document);
    }

    public static TheoryData<FieldValue, FieldValue, FieldSpec> DistinctValues => new()
    {
        { FieldValue.Of((Int128)ulong.MaxValue), FieldValue.Of((Int128)ulong.MaxValue - 1), IntegerSpec.UInt64 },
        { FieldValue.ExactNumber("1.0"), FieldValue.ExactNumber("1.00"), new ExactNumberSpec() },
        { FieldValue.Null, FieldValue.Of("null"), new NullableSpec(new TextSpec()) },
        { FieldValue.Of(1d), FieldValue.Of(1.0000000000000002d), new FloatSpec() },
        { FieldValue.Union("a", FieldValue.Of("same")), FieldValue.Union("b", FieldValue.Of("same")),
            new UnionSpec([new("a", "A", new TextSpec()), new("b", "B", new TextSpec())]) },
    };

    [Theory]
    [MemberData(nameof(DistinctValues))]
    public void DistinctTypedRepresentationsKeepTheirExactMutation(FieldValue before, FieldValue after, FieldSpec spec)
    {
        using var document = new ScalarEffectDocument(before, spec);
        using var fixture = Open(document);
        var bytes = Bytes(document);
        var result = fixture.Session.ApplyEdits([Edit(after)]);
        Assert.True(result.Ok);
        Assert.Single(result.Changes);
        Assert.Equal(after, document.GetValue("value"));
        Assert.Equal(1, result.State.Revision);
        Assert.True(fixture.Session.Undo().Ok);
        AssertBytes(bytes, document);
    }

    private static MutationSessionFixture Open(ScalarEffectDocument document)
    {
        var fixture = new MutationSessionFixture(document, openOriginal: () =>
            new ScalarEffectDocument(FieldValue.Of(17L), IntegerSpec.Int64));
        fixture.Session.BuildView();
        return fixture;
    }

    private static IncomingEdit Edit(FieldValue value, string id = "value") => new(id, JsonSerializer.SerializeToElement(value));
    private static FileOutput[] Bytes(ISaveDocument document) => document.Serialize().Select(output => output with { Content = output.Content.ToArray() }).ToArray();
    private static void AssertBytes(IReadOnlyList<FileOutput> expected, ISaveDocument document)
    {
        var actual = document.Serialize();
        Assert.Equal(expected.Select(output => output.RelativePath), actual.Select(output => output.RelativePath));
        for (var index = 0; index < expected.Count; index++) Assert.Equal(expected[index].Content.ToArray(), actual[index].Content.ToArray());
    }
}
