using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class DecoderCorruptionTests
{
    [Theory]
    [InlineData("held-common", true)]
    [InlineData("held-key", true)]
    [InlineData("storage-common", true)]
    [InlineData("storage-key", true)]
    [InlineData("projectiles", true)]
    [InlineData("regions", true)]
    [InlineData("acquisition", true)]
    [InlineData("held-common", false)]
    [InlineData("held-key", false)]
    [InlineData("storage-common", false)]
    [InlineData("storage-key", false)]
    [InlineData("projectiles", false)]
    [InlineData("regions", false)]
    [InlineData("acquisition", false)]
    public void InvalidCountsAreRejectedBeforePublishingAnyEvidence(string target, bool acceptValues)
    {
        var fixture = new SemanticFixture();
        var acquisition = SemanticReader.Read(fixture.Save.Bytes, EldenRingParser.Parse(fixture.Save.Bytes).Slots[0])
            .Ranges.Single(range => range.Name == SemanticLayout.AcquisitionRangeName).Offset;
        var offset = target switch
        {
            "held-common" => fixture.Held,
            "held-key" => fixture.Held + sizeof(uint) + SemanticLayout.HeldCommonCapacity * SemanticLayout.InventoryRowSize,
            "storage-common" => fixture.Storage,
            "storage-key" => fixture.Storage + sizeof(uint) + SemanticLayout.StorageCommonCapacity * SemanticLayout.InventoryRowSize,
            "projectiles" => fixture.ProjectileCount,
            "regions" => fixture.RegionCount,
            _ => acquisition,
        };
        fixture.Save.Write32(offset, uint.MaxValue);
        Assert.Throws<SaveFormatException>(() => fixture.Save.Open());
        var sink = new RecordingSink(acceptValues);
        Assert.Throws<SaveFormatException>(() => new EldenRingEvidenceDecoder().Decode(fixture.Save.File(), sink));
        Assert.Equal(0, sink.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CorruptionInTheLastSlotIsValidatedBeforePublishingTheFirstSlot(bool acceptValues)
    {
        var fixture = new SyntheticSave([0, EldenRingLayout.SlotCount - 1]);
        fixture.Write32(fixture.Players[EldenRingLayout.SlotCount - 1] + 0x3A4, uint.MaxValue);
        var sink = new RecordingSink(acceptValues);
        Assert.Throws<SaveFormatException>(() => new EldenRingEvidenceDecoder().Decode(fixture.File(), sink));
        Assert.Equal(0, sink.Calls);
    }

    private sealed class RecordingSink(bool acceptValues) : ISaveDecodeSink
    {
        public int MaxValues => acceptValues ? int.MaxValue : 0;
        public int MaxValueLength => 256;
        public int MaxDepth => 64;
        public long MaxFileBytes => long.MaxValue;
        public int Calls { get; private set; }
        public bool Add(string path, string kind, string value, string? label = null)
        {
            Calls++;
            return acceptValues;
        }
        public void Warn(string message) => Assert.Fail(message);
        public void MarkTruncated(string reason) => Assert.Fail(reason);
    }
}
