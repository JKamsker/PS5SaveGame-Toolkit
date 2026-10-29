using System.Buffers.Binary;
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

    [Theory]
    [InlineData("constant", "gameConstantStatModifier", "-3.5", "Raw modifier operation 2; Final effect and named stat mapping unverified.")]
    [InlineData("combined", "gameCombinedStatModifier", "1.25", "Raw modifier operation 2; reference stat 0x8877665544332211; combination 3; relation 4; Final effect and named stat mapping unverified.")]
    [InlineData("curve", "gameCurveStatModifier", "movement / sprint", "Raw modifier operation 2; curve stat 0xFFEEDDCCBBAA0099; opaque uint32 17; Final effect and named stat mapping unverified.")]
    public void ModifierBufferUsesExactClassAndFieldWidths(string kind, string type, string value, string details)
    {
        var row = Assert.Single(Read(ModifierBufferFixture.Buffer(Record(kind))));
        Assert.Equal("item-stats/54/root/modifiersBuffer/1", row.Key);
        Assert.Equal("54", row.Values["node"]);
        Assert.Equal("root", row.Values["part"]);
        Assert.Equal("modifiersBuffer", row.Values["buffer"]);
        Assert.Equal(type, row.Values["class"]);
        Assert.Equal(value, row.Values["value"]);
        Assert.Equal("0x1122334455667788", row.Values["stat"]);
        Assert.Equal(details, row.Values["details"]);
    }

    [Fact]
    public void MixedRecordsStayAlignedAndUseDistinctStableKeys()
    {
        var rows = Read(ModifierBufferFixture.Buffer(ModifierBufferFixture.Constant(), ModifierBufferFixture.Combined(),
            ModifierBufferFixture.Curve(), ModifierBufferFixture.Constant(7)));
        Assert.Equal(["-3.5", "1.25", "movement / sprint", "7"], rows.Select(row => row.Values["value"]));
        Assert.Equal(Enumerable.Range(1, 4).Select(index => $"item-stats/54/root/modifiersBuffer/{index}"), rows.Select(row => row.Key));
        Assert.Empty(Read(ModifierBufferFixture.Buffer()));
    }

    [Theory]
    [InlineData(false, float.PositiveInfinity)]
    [InlineData(false, float.NegativeInfinity)]
    [InlineData(false, float.NaN)]
    [InlineData(true, float.PositiveInfinity)]
    [InlineData(true, float.NegativeInfinity)]
    [InlineData(true, float.NaN)]
    public void NonfiniteConstantAndCombinedValuesAreRejectedIndependently(bool combined, float value)
    {
        var record = combined ? ModifierBufferFixture.Combined(value) : ModifierBufferFixture.Constant(value);
        Assert.Equal("Nonfinite stat modifier.", Reject(ModifierBufferFixture.Buffer(record)).Message);
    }

    [Theory]
    [InlineData("constant")]
    [InlineData("combined")]
    [InlineData("curve")]
    public void EveryRecordTruncationIsRejectedWithAnOtherwiseCorrectBufferLength(string kind)
    {
        var record = Record(kind);
        for (var length = 1; length < record.Length; length++)
        {
            // Repair the outer length so rejection must come from the truncated record, not its wrapper.
            var error = Reject(ModifierBufferFixture.Buffer(record[..length]));
            Assert.StartsWith("CSAV range at 0x", error.Message);
        }
        Assert.Single(Read(ModifierBufferFixture.Buffer(record)));
    }

    [Theory]
    [InlineData("constant")]
    [InlineData("combined")]
    [InlineData("curve")]
    public void UnknownClassAndUnexplainedSuffixAreRejectedFromFreshValidRecords(string kind)
    {
        var record = Record(kind);
        var unknown = record.ToArray();
        unknown[1] = (byte)'X'; // Preserve the packed class-name length and every following field.
        Assert.Equal("Unverified modifier class; remaining buffer preserved.", Reject(ModifierBufferFixture.Buffer(unknown)).Message);
        Assert.StartsWith("CSAV range at 0x", Reject(ModifierBufferFixture.Buffer([.. record, 0])).Message);
        Assert.Single(Read(ModifierBufferFixture.Buffer(record)));
    }

    [Fact]
    public void BufferLengthDeclarationIsCheckedIndependentlyOfRecordCorruption()
    {
        var valid = ModifierBufferFixture.Buffer(ModifierBufferFixture.Constant());
        for (var length = 0; length < sizeof(int); length++)
            Assert.Equal("CSAV range at 0x0 exceeds its containing data.", Reject(valid[..length]).Message);
        foreach (var declared in new[] { -1, 0, valid.Length - 5, valid.Length - 3, int.MaxValue })
        {
            var bytes = valid.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(bytes, declared);
            Assert.Equal("Modifier buffer length mismatch.", Reject(bytes).Message);
        }
        Assert.Single(Read(valid));
    }

    [Theory]
    [InlineData(4096, true)]
    [InlineData(4097, false)]
    public void ModifierCountHasAnExactBoundWithCompleteRecords(int count, bool accepted)
    {
        var bytes = ModifierBufferFixture.Buffer(Enumerable.Repeat(ModifierBufferFixture.Constant(), count).ToArray());
        if (accepted)
        {
            var rows = Read(bytes);
            Assert.Equal(count, rows.Count);
            Assert.Equal(count, rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
        }
        else Assert.Equal("Modifier count exceeds inspection limit.", Reject(bytes).Message);
    }

    private static byte[] Record(string kind) => kind switch
    {
        "constant" => ModifierBufferFixture.Constant(),
        "combined" => ModifierBufferFixture.Combined(),
        "curve" => ModifierBufferFixture.Curve(),
        _ => throw new ArgumentException("Unknown modifier fixture kind.", nameof(kind)),
    };

    private static List<InspectionRow> Read(byte[] bytes)
    {
        var rows = new List<InspectionRow>();
        ItemStatsInspection.ReadBuffer(bytes, 54, "root", "modifiersBuffer", rows);
        return rows;
    }

    private static SaveFormatException Reject(byte[] bytes) => Assert.Throws<SaveFormatException>(() => Read(bytes));
}
