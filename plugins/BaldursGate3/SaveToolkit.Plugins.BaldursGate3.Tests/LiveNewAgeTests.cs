using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class LiveNewAgeTests
{
    [Fact]
    public void ExplicitMultiContributionMembersUseTheirOwnIndicesAndPreserveUnknownPadding()
    {
        var original = LiveNewAgeFixture.Create();
        var source = LiveNewAgeFixture.Source(original);
        Assert.Empty(source.Diagnostics);
        Assert.Equal([5L, 4L], source.Stacks.Select(s => s.Original));
        var number = source.Numbers.First(n => n.Family == "Stacks");
        source.Set(number, FieldValue.Of(2L));
        Assert.Equal(2, source.Get(number));
        var edited = source.Write();
        var reopened = LiveNewAgeFixture.Source(edited);
        Assert.Equal(2, reopened.Stacks[0].Original);
        Assert.Equal(4, reopened.Stacks[1].Original);
        var allowed = number.Offsets.SelectMany(offset => Enumerable.Range(offset, 4)).ToHashSet();
        Assert.All(Enumerable.Range(0, original.Length).Where(i => original[i] != edited[i]), i => Assert.Contains(i, allowed));
        Assert.Equal([1u, 1u], number.Offsets.Select(offset => BinaryData.U32(edited, offset)));
        source.Set(number, FieldValue.Of(number.Original));
        Assert.Equal(original, source.Write());
    }

    [Theory]
    [InlineData("Stacks")]
    [InlineData("Health")]
    [InlineData("Stats")]
    public void EveryNewScalarFamilyRejectsBoundsWithoutMutatingThenReopensExactly(string family)
    {
        var source = LiveNewAgeFixture.Source();
        var original = source.Write();
        var number = source.Numbers.First(n => n.Family == family);
        foreach (var next in new[] { number.Min - 1, number.Max + 1, long.MaxValue })
        {
            Assert.Throws<SaveFormatException>(() => source.Set(number, FieldValue.Of(next)));
            Assert.Equal(original, source.Write());
        }
        source.Set(number, FieldValue.Of(number.Original - 1));
        var bytes = source.Write();
        Assert.Equal(number.Original - 1, source.Get(number));
        Assert.Equal(number.Original - 1, LiveNewAgeFixture.Source(bytes).Numbers.Single(n => n.Key == number.Key).Original);
        var diff = ByteDiffer.Compare(original, bytes, options: new ByteDiffOptions { MergeGap = 0, ContextBytes = 0 });
        Assert.NotEmpty(diff.Ranges);
        var allowed = number.Offsets.SelectMany(offset => Enumerable.Range(offset, sizeof(uint))).ToHashSet();
        Assert.All(diff.Ranges.SelectMany(range => Enumerable.Range((int)range.Offset, (int)range.Length)),
            offset => Assert.Contains(offset, allowed));
    }

    [Fact]
    public void ReorderingChangesOnlyTwoExistingMapKeysAndRestoresExactBytes()
    {
        var source = LiveNewAgeFixture.Source();
        var original = source.Write();
        var before = source.Capture();
        source.Swap(source.Slots[0], source.Slots[1]);
        Assert.Equal([5, 4], source.Slots.Select(source.Slot));
        var edited = source.Write();
        Assert.Equal([5, 4], LiveNewAgeFixture.Source(edited).Slots.Select(s => s.Slot));
        var changed = Enumerable.Range(0, original.Length).Where(i => original[i] != edited[i]).ToArray();
        Assert.Equal(source.Slots.Select(s => s.KeyOffset), changed);
        source.Restore(before);
        Assert.Equal(original, source.Write());
    }

    [Fact]
    public void BadPointersIndicesAliasingAndMissingContributionsDisableTheAffectedFamily()
    {
        var source = LiveNewAgeFixture.Source();
        var groups = source.Access.Model.Components.Single(c => c.Name.EndsWith("NewStackComponent", StringComparison.Ordinal));
        var entries = source.Access.Model.Components.Single(c => c.Name.EndsWith("StackEntry", StringComparison.Ordinal));
        foreach (var mutate in new Action<byte[]>[]
        {
            bytes => LsmfFixture.U64(bytes, groups.Offset, ulong.MaxValue),
            bytes => LsmfFixture.U32(bytes, entries.Offset, ushort.MaxValue),
            bytes => LsmfFixture.U32(bytes, entries.Offset + 4, 0),
            bytes => LsmfFixture.U32(bytes, entries.Offset + 16, 0), // second member now has no contribution
        })
        {
            var bytes = source.Access.Model.Bytes.ToArray();
            mutate(bytes);
            var malformed = LiveNewAgeFixture.Source(bytes);
            Assert.Empty(malformed.Stacks);
            Assert.NotEmpty(malformed.Diagnostics);
            Assert.Equal(2, malformed.Slots.Count);
            Assert.Equal(bytes, malformed.Write());
        }
    }
}
