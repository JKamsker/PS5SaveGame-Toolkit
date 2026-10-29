using System.Buffers.Binary;
using System.Text;
using System.Runtime.InteropServices;
using SaveToolkit.Sdk;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

internal static class WriteAssertions
{
    public static void FlipReturnedByte(ReadOnlyMemory<byte> memory, int offset = 0)
    {
        Assert.InRange(offset, 0, memory.Length - 1);
        Assert.True(MemoryMarshal.TryGetArray(memory, out var segment));
        segment.Array![segment.Offset + offset] ^= 0xFF;
    }

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

    public static void EditedQuantitiesMatch(SyntheticSave save, ISaveDocument document, uint target)
    {
        var expected = save.Bytes.ToArray();
        var resource = save.Inventory[save.ResourceName] + save.QuantityRelativeOffset;
        var ammo = save.Inventory["ArrowNormal"] + save.QuantityRelativeOffset;
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(resource), target);
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(ammo), target);
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(ammo + sizeof(uint)), target);
        var checksum = save.IsYotei ? YoteiTestData.Checksum(expected) : SuckerPunchTestData.Checksum(expected);
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(save.ChecksumOffset), checksum);
        var output = Assert.Single(document.Serialize());
        var actual = output.Content.ToArray();
        Assert.Equal(save.File().RelativePath, output.RelativePath);
        Assert.Equal(expected, actual);
        OnlyRangesChanged(save.Bytes, actual,
            [(save.ChecksumOffset, sizeof(uint)), (resource, sizeof(uint)), (ammo, 2 * sizeof(uint))]);
        using var reopened = SuckerPunchTestData.Open(SuckerPunchTestData.WithBytes(actual,
            save.IsYotei ? YoteiTestData.Title : SuckerPunchTestData.TsushimaTitle));
        Assert.Equal(target, reopened.GetValue(save.ResourceId).AsUInt32());
        Assert.Equal(target, reopened.GetValue(save.ArrowId).AsUInt32());
        Assert.Equal(target, reopened.GetValue(save.ArrowId + "-copy").AsUInt32());
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
