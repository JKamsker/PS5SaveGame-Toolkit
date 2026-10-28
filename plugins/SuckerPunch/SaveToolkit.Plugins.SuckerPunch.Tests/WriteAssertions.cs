using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

internal static class WriteAssertions
{
    public static int CountOffset(byte[] bytes, string name)
    {
        var anchor = bytes.AsSpan().IndexOf("\x11\0\0\0LegendsInvestment"u8);
        Assert.True(anchor >= 0);
        var key = new byte[sizeof(uint) + name.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(key, (uint)name.Length);
        Encoding.ASCII.GetBytes(name, key.AsSpan(sizeof(uint)));
        var relative = bytes.AsSpan(anchor).IndexOf(key);
        Assert.True(relative >= 0);
        return anchor + relative + key.Length + sizeof(uint);
    }

    public static void OnlyRangesChanged(byte[] before, byte[] after, IReadOnlyList<(int Start, int Length)> allowed)
    {
        var diff = ByteDiffer.Compare(before, after, options: new ByteDiffOptions { MergeGap = 0 });
        Assert.Equal(before.Length, after.Length);
        Assert.False(diff.Truncated);
        Assert.All(diff.Ranges, range => Assert.Contains(allowed,
            span => range.Offset >= span.Start && range.End <= span.Start + span.Length));
    }
}
