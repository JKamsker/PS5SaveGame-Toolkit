using SaveToolkit.Testing.Diff;
using static SaveToolkit.SaveDiff.Tests.TestSaves;

namespace SaveToolkit.SaveDiff.Tests;

public sealed class ByteDifferTests
{
    [Fact]
    public void IdenticalContent_HasNoChanges()
    {
        var data = Bytes(1000, 7);

        var result = ByteDiffer.Compare(data, data.ToArray());

        Assert.False(result.HasChanges);
        Assert.Equal(0, result.RangeCount);
        Assert.Equal(1000, result.AfterLength);
    }

    [Fact]
    public void NearbyDifferences_AreMergedWithinGap_AndCountedExactly()
    {
        var before = Bytes(200);
        var after = With(before, 10, 1, 0, 0, 2); // differences at 10 and 13 (gap 2)
        after[100] = 9;

        var result = ByteDiffer.Compare(before, after, options: new ByteDiffOptions { MergeGap = 4 });

        Assert.Equal(2, result.RangeCount);
        Assert.Equal(new ByteChange(10, 4, ByteChangeKind.Modified), result.Ranges[0] with { DifferingBytes = 0, WindowStart = 0, BeforeHex = null, AfterHex = null });
        Assert.Equal(2, result.Ranges[0].DifferingBytes);
        Assert.Equal(3, result.ChangedBytes);
        Assert.Equal(100, result.Ranges[1].Offset);
    }

    [Fact]
    public void HexPreview_IncludesContextAroundTheChange()
    {
        var before = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
        var after = With(before, 32, 0xFF);

        var range = Assert.Single(ByteDiffer.Compare(before, after, options: new ByteDiffOptions { ContextBytes = 2 }).Ranges);

        Assert.Equal(30, range.WindowStart);
        Assert.Equal("1e1f202122", range.BeforeHex);
        Assert.Equal("1e1fff2122", range.AfterHex);
    }

    [Fact]
    public void GrowthAndShrinkage_AreReportedAsTailRanges()
    {
        var shortData = Bytes(10, 1);
        var longData = Bytes(25, 1);

        var grown = ByteDiffer.Compare(shortData, longData);
        var shrunk = ByteDiffer.Compare(longData, shortData);

        var appended = Assert.Single(grown.Ranges);
        Assert.Equal((10L, 15L, ByteChangeKind.Appended), (appended.Offset, appended.Length, appended.Kind));
        Assert.Equal((10L, 25L), (grown.BeforeLength, grown.AfterLength));
        Assert.Equal(ByteChangeKind.Truncated, Assert.Single(shrunk.Ranges).Kind);
        Assert.Equal(15, shrunk.ChangedBytes);
    }

    [Fact]
    public void ChangesAcrossBufferBoundaries_AreFoundInLargeFiles()
    {
        var before = Bytes(300_000);
        var after = With(before, 65_535, 1, 1); // straddles the 64 KiB scan buffer
        after[299_999] = 1;

        var result = ByteDiffer.Compare(before, after);

        Assert.Equal(2, result.RangeCount);
        Assert.Equal(65_535, result.Ranges[0].Offset);
        Assert.Equal(2, result.Ranges[0].Length);
        Assert.Equal(299_999, result.Ranges[1].Offset);
    }

    [Fact]
    public void MaskedBytes_AreCountedAsIgnored_NotChanged()
    {
        var before = Bytes(64);
        var after = With(before, 16, 1, 2, 3, 4);
        after[40] = 5;

        var result = ByteDiffer.Compare(before, after, [new ByteInterval(16, 20)]);

        Assert.Equal(4, result.IgnoredBytes);
        Assert.Equal(1, result.ChangedBytes);
        Assert.Equal(40, Assert.Single(result.Ranges).Offset);
    }

    [Fact]
    public void RangeCap_KeepsMemoryBounded_ButCountsEverything()
    {
        var before = Bytes(10_000);
        var after = before.ToArray();
        for (var i = 0; i < after.Length; i += 100)
        {
            after[i] = 1;
        }

        var result = ByteDiffer.Compare(before, after, options: new ByteDiffOptions { MaxRanges = 5 });

        Assert.Equal(100, result.RangeCount);
        Assert.Equal(5, result.Ranges.Count);
        Assert.True(result.Truncated);
        Assert.Equal(100, result.ChangedBytes);
    }
}
