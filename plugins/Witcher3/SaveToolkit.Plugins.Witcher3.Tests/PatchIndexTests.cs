using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class PatchIndexTests
{
    [Fact]
    public void IndexedRangesMatchIndependentIntervalOracleIncludingShrinksAndCrossings()
    {
        Sav3Patch[] patches = [new(4, 3, [1]), new(10, 2, [1, 2, 3, 4]), new(16, 4, [1, 2, 3, 4])];
        var index = new Sav3PatchIndex(patches);
        for (var offset = 0; offset <= 24; offset++)
        {
            Assert.Equal(offset + patches.Where(patch => patch.End <= offset).Sum(patch => patch.Growth), index.Relocate(offset));
            for (var length = 1; length <= 24 - offset; length++)
            {
                var end = offset + length;
                var overlapping = patches.Where(patch => patch.Offset < end && patch.End > offset).ToArray();
                if (overlapping.Any(patch => patch.Offset < offset || patch.End > end))
                    Assert.Throws<SaveFormatException>(() => index.GrowthInside(offset, length));
                else
                    Assert.Equal(overlapping.Sum(patch => patch.Growth), index.GrowthInside(offset, length));
                Assert.Equal(patches.Any(patch => patch.Offset == offset && patch.Length == length), index.Replaces(offset, length));
            }
        }
    }

    [Fact]
    public void EmptyIndexLeavesOffsetsAndLengthsUnchanged()
    {
        var index = new Sav3PatchIndex([]);
        Assert.Equal(42, index.Relocate(42));
        Assert.Equal(0, index.GrowthInside(16, 12));
        Assert.False(index.Replaces(16, 12));
    }
}
