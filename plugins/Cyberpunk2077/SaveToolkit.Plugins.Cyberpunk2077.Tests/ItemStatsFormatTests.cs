using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class ItemStatsFormatTests
{
    [Theory]
    [InlineData(0UL, 0U, (ushort)0, 0UL)]
    [InlineData(0x1A9261AD92UL, 1376065433U, (ushort)0, 0x6791B5FCBF04953FUL)]
    [InlineData(0x1234567890UL, 0xABCDEF01U, (ushort)42, 0x4652165751887B4EUL)]
    [InlineData(ulong.MaxValue, uint.MaxValue, ushort.MaxValue, 0x4F673F77FAC1E6B1UL)]
    public void ItemHashMatchesIndependentModularArithmeticVectors(ulong tweak, uint seed, ushort counter, ulong expected) =>
        Assert.Equal(expected, ItemStatsInspection.InstanceHash(new ItemIdentity(tweak, seed, counter, 0)));

    [Fact]
    public void ModifierBufferUsesExactClassAndFieldWidthsAndRejectsUnknownRemainders()
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write(0); // Buffer size is filled after construction.
        const string name = "gameConstantStatModifier";
        writer.Write((byte)(0x80 | name.Length));
        writer.Write(Encoding.UTF8.GetBytes(name));
        writer.Write(0x1122334455667788UL);
        writer.Write(2U);
        writer.Write(-3.5F);
        writer.Flush();
        var bytes = output.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes, bytes.Length - 4);
        var rows = new List<InspectionRow>();
        ItemStatsInspection.ReadBuffer(bytes, 54, "root", "modifiersBuffer", rows);
        var row = Assert.Single(rows);
        Assert.Equal("-3.5", row.Values["value"]);
        Assert.Equal("0x1122334455667788", row.Values["stat"]);
        Assert.Contains("operation 2", row.Values["details"]);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(bytes.Length - 4), float.PositiveInfinity);
        Assert.Throws<SaveFormatException>(() => ItemStatsInspection.ReadBuffer(bytes, 54, "root", "modifiersBuffer", []));
        bytes[5] = (byte)'X';
        Assert.Throws<SaveFormatException>(() => ItemStatsInspection.ReadBuffer(bytes, 54, "root", "modifiersBuffer", []));
        bytes[0] ^= 1;
        Assert.Throws<SaveFormatException>(() => ItemStatsInspection.ReadBuffer(bytes, 54, "root", "modifiersBuffer", []));
    }
}
