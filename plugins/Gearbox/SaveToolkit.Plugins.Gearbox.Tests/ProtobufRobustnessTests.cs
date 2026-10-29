using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ProtobufRobustnessTests
{
    [Theory]
    [InlineData("9A068180808010AA")] // Length 2^32 + 1 must not wrap to one byte.
    [InlineData("988680801001")] // Tag 2^32 + 792 must not wrap to field 99.
    [InlineData("980680808080808080808002")] // Value exceeds UInt64.
    [InlineData("9B060E9C06")] // Reserved wire type inside an opaque group.
    [InlineData("9B060F9C06")]
    public void CorruptWireFraming_IsRejected(string hex) =>
        Assert.Throws<SaveFormatException>(() => Parse(Convert.FromHexString(hex)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldBudget_AlsoCountsOpaqueGroupFieldsAndPackedElements(bool packed)
    {
        // The existing parser budget is 250,000 fields across the entire document.
        byte[] bytes = packed
            ? [0x62, 0x91, 0xA1, 0x0F, .. new byte[250_001]] // Character #12 packed int32.
            : [0x9B, 0x06, .. Enumerable.Repeat(new byte[] { 0x08, 0 }, 250_001).SelectMany(b => b), 0x9C, 0x06];
        Assert.Throws<SaveFormatException>(() => Parse(bytes));
    }

    [Fact]
    public void GroupDepth_UsesTheSameFortyLevelLimitAsMessages()
    {
        var bytes = Enumerable.Repeat(new byte[] { 0x9B, 0x06 }, 41)
            .Concat(Enumerable.Repeat(new byte[] { 0x9C, 0x06 }, 41)).SelectMany(b => b).ToArray();
        Assert.Throws<SaveFormatException>(() => Parse(bytes));
    }

    [Theory]
    [InlineData("9886008100")] // Noncanonical, representable tag and scalar.
    [InlineData("9A068100AA")] // Noncanonical, representable length.
    [InlineData("9B060881009C06")] // Unknown group stays opaque and lossless.
    public void SupportedOpaqueAndNoncanonicalRecords_StayByteIdentical(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        Assert.Equal(bytes, Parse(bytes).Encode());
    }

    private static ProtoMessage Parse(byte[] bytes) => ProtoMessage.Parse(bytes, GearboxSchema.Bl3, "Character");

    [Theory]
    [InlineData("9A06FFFFFFFF07")]
    [InlineData("9A06FFFFFFFF0F")]
    [InlineData("9A0680")]
    [InlineData("9B06")]
    [InlineData("9B069406")]
    [InlineData("9C06")]
    [InlineData("620180")]
    public void TruncatedLengthsPackedValuesAndGroups_RejectWithBoundedAllocation(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        _ = GearboxSchema.Bl3; // Exclude one-time schema initialization from allocation observation.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var error = Assert.Throws<SaveFormatException>(() => Parse(bytes));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 128 * 1024);
    }

    [Fact]
    public void FortyNestedGroups_RemainLossless()
    {
        var bytes = Enumerable.Repeat(new byte[] { 0x9B, 0x06 }, 40)
            .Concat(Enumerable.Repeat(new byte[] { 0x9C, 0x06 }, 40)).SelectMany(b => b).ToArray();
        Assert.Equal(bytes, Parse(bytes).Encode());
    }

    [Fact]
    public void PackedElementBudget_IsSharedAcrossRecords()
    {
        byte[] record = [0x62, 0xC0, 0xA9, 0x07, .. new byte[120_000]];
        Assert.Equal(record, Parse(record).Encode());
        Assert.Throws<SaveFormatException>(() => Parse([.. record, .. record, .. record]));
    }
}
