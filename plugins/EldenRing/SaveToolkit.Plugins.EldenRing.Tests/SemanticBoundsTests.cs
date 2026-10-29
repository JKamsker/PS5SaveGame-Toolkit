using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class SemanticBoundsTests
{
    [Theory]
    [InlineData("held-common", 2688)]
    [InlineData("held-key", 384)]
    [InlineData("storage-common", 1920)]
    [InlineData("storage-key", 128)]
    [InlineData("acquisition", 7000)]
    public void FixedCountsAcceptTheirExactCapacityAndRejectOverflow(string target, uint capacity)
    {
        var fixture = new SemanticFixture();
        var slot = EldenRingParser.Parse(fixture.Save.Bytes).Slots[0];
        var original = SemanticReader.Read(fixture.Save.Bytes, slot);
        var offset = target switch
        {
            "held-common" => fixture.Held,
            "held-key" => fixture.Held + sizeof(uint) + SemanticLayout.HeldCommonCapacity * SemanticLayout.InventoryRowSize,
            "storage-common" => fixture.Storage,
            "storage-key" => fixture.Storage + sizeof(uint) + SemanticLayout.StorageCommonCapacity * SemanticLayout.InventoryRowSize,
            _ => original.Ranges.Single(range => range.Name == SemanticLayout.AcquisitionRangeName).Offset,
        };
        foreach (var count in new[] { 0U, capacity })
        {
            fixture.Save.Write32(offset, count);
            _ = SemanticReader.Read(fixture.Save.Bytes, slot);
            Assert.Equal(count, EldenRingParser.ReadUInt32(fixture.Save.Bytes, offset));
        }
        foreach (var count in new uint[] { capacity + 1, int.MaxValue, 0x80000000U, uint.MaxValue })
        {
            fixture.Save.Write32(offset, count);
            var error = Assert.Throws<SaveFormatException>(() => SemanticReader.Read(fixture.Save.Bytes, slot));
            Assert.Contains("capacity", error.Message);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VariableCountsCannotConsumeTheSlotOrWrapTheirByteLength(bool regions)
    {
        var fixture = new SemanticFixture();
        var slot = EldenRingParser.Parse(fixture.Save.Bytes).Slots[0];
        var offset = regions ? fixture.RegionCount : fixture.ProjectileCount;
        var stride = regions ? sizeof(uint) : SemanticLayout.ProjectileSize;
        var remaining = slot.Offset + EldenRingLayout.SlotSize - offset - sizeof(uint);
        var maximumRemaining = (uint)(remaining / stride);
        uint[] counts = [maximumRemaining, maximumRemaining + 1, int.MaxValue, 0x80000000U, uint.MaxValue];
        foreach (var count in counts)
        {
            fixture.Save.Write32(offset, count);
            // Even a count whose records just fit must leave room for required following ranges.
            var error = Assert.Throws<SaveFormatException>(() => SemanticReader.Read(fixture.Save.Bytes, slot));
            Assert.Contains("character slot", error.Message);
        }
    }

    [Fact]
    public void MenuLengthAllowsTheLastProfileToEndExactlyAtTheRegulationBoundary()
    {
        const int summaryBytes = SyntheticSave.SlotCount * (SyntheticSave.ProfileSize + 1);
        const int maximumMenuLength = EldenRingLayout.UserDataSize - 0x14C - 8 - summaryBytes;
        var fixture = new SyntheticSave([0, 9], menuSize: maximumMenuLength);
        using var document = fixture.Open();
        Assert.Equal(EldenRingLayout.RegulationOffset, fixture.Profile(9) + SyntheticSave.ProfileSize);
        Assert.True(fixture.Bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
        foreach (var length in new uint[] { (uint)maximumMenuLength + 1, int.MaxValue, 0x80000000U, uint.MaxValue })
        {
            fixture.Write32(SyntheticSave.Menu + sizeof(uint), length);
            var error = Assert.Throws<SaveFormatException>(() => EldenRingParser.Parse(fixture.Bytes));
            Assert.Contains("menu-data length", error.Message);
        }
    }
}
