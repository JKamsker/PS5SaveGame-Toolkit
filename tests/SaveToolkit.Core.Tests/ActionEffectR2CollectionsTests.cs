using System.Collections;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Core.Tests.SdkHelpers;

namespace SaveToolkit.Core.Tests;

public sealed class ActionEffectR2CollectionsTests
{
    [Fact]
    public void CountLyingNestedPathsAndOptionsStopAtTheFirstInconsistentElement()
    {
        var path = new MiscountedList<string>(1, Enumerable.Repeat("", 100_000));
        Assert.Throws<InvalidOperationException>(() => ActionPlanSnapshots.Snapshot(Plan(path)));
        Assert.Equal(2, path.Visits);

        var choices = new MiscountedList<EnumOption>(1, Enumerable.Repeat(new EnumOption("", ""), 100_000));
        var flags = new MiscountedList<FlagBit>(1, Enumerable.Repeat(new FlagBit(0, ""), 100_000));
        var unions = new MiscountedList<UnionOption>(1, Enumerable.Repeat(new UnionOption("", "", new BoolSpec()), 100_000));
        Assert.False(FieldSpecMetadata.TryEstimate(new EnumSpec(choices), long.MaxValue, out _));
        Assert.False(FieldSpecMetadata.TryEstimate(new FlagsSpec(flags, 32), long.MaxValue, out _));
        Assert.False(FieldSpecMetadata.TryEstimate(new UnionSpec(unions), long.MaxValue, out _));
        Assert.Equal(2, choices.Visits);
        Assert.Equal(2, flags.Visits);
        Assert.Equal(2, unions.Visits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PathCodecBoundsInputMappedAndInverseBeforeFurtherDispatch(int malformedStage)
    {
        using var document = new MemberAdapterDocument();
        var path = new MiscountedList<string>(1, Enumerable.Repeat("", 100_000));
        var forwards = 0;
        var inverses = 0;
        var adapter = new DocumentMemberAdapter("member", document, new DocumentMemberIdentityPolicy
        {
            NavigationPath = (_, input) => { forwards++; return malformedStage == 1 ? path : input; },
            LocalNavigationPath = (_, input) => { inverses++; return malformedStage == 2 ? path : input; },
        });
        Assert.Throws<InvalidOperationException>(() => adapter.ToGroupPlan(Plan(malformedStage == 0 ? path : ["local"])));
        Assert.Equal(2, path.Visits);
        Assert.Equal(malformedStage == 0 ? 0 : 1, forwards);
        Assert.Equal(malformedStage == 2 ? 1 : 0, inverses);
    }

    [Fact]
    public void CancellationDuringNestedTraversalStopsBeforeCopyOrCodec()
    {
        using var cancellation = new CancellationTokenSource();
        var choices = new MiscountedList<EnumOption>(100, Enumerable.Range(0, 100).Select(index =>
        {
            if (index == 2) cancellation.Cancel();
            return new EnumOption(index.ToString(System.Globalization.CultureInfo.InvariantCulture), "Choice");
        }));
        Assert.Throws<OperationCanceledException>(() => FieldSpecMetadata.SnapshotBounded(new EnumSpec(choices), cancellation.Token));
        Assert.Equal(3, choices.Visits);
        using var document = new MemberAdapterDocument();
        var calls = 0;
        var adapter = new DocumentMemberAdapter("member", document, new DocumentMemberIdentityPolicy
        {
            NavigationPath = (_, path) => { calls++; return path; },
        });
        Assert.Throws<OperationCanceledException>(() => adapter.ToGroupPlan(Plan(["local"]), cancellation.Token));
        Assert.Equal(0, calls);
    }

    private static ActionPlan Plan(IReadOnlyList<string> path) => new()
    {
        Changes = [new PlannedChange("effect", "Effect", FieldValue.Of(1), FieldValue.Of(2))
        {
            Effect = new(ActionEffectKind.Observation, IntegerSpec.Int32 with { ReadOnly = true }) { Path = path },
        }],
    };
}

internal sealed class MiscountedList<T>(int count, IEnumerable<T> values) : IReadOnlyList<T>
{
    public int Count => count;
    public int Visits { get; private set; }
    public T this[int index] => values.ElementAt(index);
    public IEnumerator<T> GetEnumerator()
    {
        foreach (var value in values) { Visits++; yield return value; }
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
