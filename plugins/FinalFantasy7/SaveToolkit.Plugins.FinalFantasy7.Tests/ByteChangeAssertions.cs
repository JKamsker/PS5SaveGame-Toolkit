using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

internal static class ByteChangeAssertions
{
    public static void OnlyRangeChanged(byte[] original, byte[] edited, int offset, int length) =>
        OnlyOffsetsChanged(original, edited, Enumerable.Range(offset, length));

    public static void OnlyOffsetsChanged(byte[] original, byte[] edited, IEnumerable<int> allowed)
    {
        var offsets = allowed.ToHashSet();
        var diff = ByteDiffer.Compare(original, edited, options: new() { MergeGap = 0 });
        Assert.True(diff.ChangedBytes > 0);
        Assert.All(diff.Ranges, range => Assert.All(Enumerable.Range((int)range.Offset, (int)range.Length),
            offset => Assert.Contains(offset, offsets)));
    }
}
