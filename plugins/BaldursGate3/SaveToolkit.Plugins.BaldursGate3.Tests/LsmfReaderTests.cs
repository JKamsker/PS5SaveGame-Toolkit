using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class LsmfReaderTests
{
    [Fact]
    public void DynamicDirectoryRetainsUnknownVersionsFlagsAndEveryOriginalByte()
    {
        var bytes = LsmfFixture.Entities();
        var original = bytes.ToArray();
        var model = LsmfReader.Read(bytes);
        Assert.Equal(2, model.Components.Count);
        Assert.Equal("unknown.v91.Component", model.Components[1].Name);
        Assert.Equal(uint.MaxValue, model.Components[1].Flags);
        Assert.Equal(0xFEDCBA9876543210UL, model.Components[1].TypeHash);
        Assert.Equal(Convert.FromHexString("DEADBEEF"), model.NominalData(model.Components[1]).ToArray());
        Assert.Equal(original, model.Bytes.ToArray());
        Assert.Equal(original, bytes);
    }

    [Fact]
    public void EntityGuidVectorKeepsDuplicateOccurrencesSeparateAndDoesNotGuessAnOwner()
    {
        var entities = LsmfReader.Read(LsmfFixture.Entities()).Entities();
        Assert.Equal(2, entities.Count);
        Assert.All(entities, entity => Assert.Equal("3ed74f06-3c60-42dc-83f6-f034cb47c679", entity.Guid));
        Assert.Equal([0, 1], entities.Select(e => e.Row));
        Assert.Equal([1, 2], entities.Select(e => e.Occurrence));
        Assert.All(entities, entity => Assert.Equal(2, entity.Copies));
        Assert.Equal(2, entities.Select(e => e.Key).Distinct().Count());
    }

    [Fact]
    public void ArenaPointersAndAbsoluteEntityPointersHaveSeparateCheckedConventions()
    {
        var model = LsmfReader.Read(LsmfFixture.Entities());
        Assert.Equal(0, model.EntityRow(48));
        Assert.Equal(1, model.EntityRow(64));
        Assert.Equal(model.NominalData(model.Components[0]).ToArray(), model.ArenaRange(0, 32, 8).ToArray());
        foreach (var pointer in new ulong[] { 0, 47, 49, 80, ulong.MaxValue })
            Assert.Throws<SaveFormatException>(() => model.EntityRow(pointer));
        Assert.Throws<SaveFormatException>(() => model.ArenaRange(ulong.MaxValue, 1));
        Assert.Throws<SaveFormatException>(() => model.ArenaRange(0, ulong.MaxValue));
        Assert.Throws<SaveFormatException>(() => model.ArenaRange(1, 1, 8));
        Assert.Throws<SaveFormatException>(() => model.ArenaRange(0, 1, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(47)]
    public void TruncatedHeadersFailWithAFormatError(int length) =>
        Assert.Throws<SaveFormatException>(() => LsmfReader.Read(LsmfFixture.Entities().AsMemory(0, length)));

    [Theory]
    [InlineData(16, ulong.MaxValue)] // pointer addition overflow
    [InlineData(24, ulong.MaxValue)] // impossible directory size
    public void HeaderRangesRejectOverflow(int offset, ulong value)
    {
        var bytes = LsmfFixture.Entities();
        LsmfFixture.U64(bytes, offset, value);
        Assert.Throws<SaveFormatException>(() => LsmfReader.Read(bytes));
    }

    [Theory]
    [InlineData(0, ulong.MaxValue)]
    [InlineData(8, 513UL)]
    [InlineData(32, ulong.MaxValue)]
    [InlineData(40, ulong.MaxValue)]
    public void DescriptorNamesCountsAndRangesAreBounded(int relative, ulong value)
    {
        var bytes = LsmfFixture.Entities();
        LsmfFixture.U64(bytes, LsmfFixture.Descriptor(bytes) + relative, value);
        Assert.Throws<SaveFormatException>(() => LsmfReader.Read(bytes));
    }

    [Fact]
    public void InvalidVariantsAlignmentUtf8AndDuplicateNamesAreRejected()
    {
        var bytes = LsmfFixture.Entities();
        bytes[7] = 9;
        Assert.Throws<SaveFormatException>(() => LsmfReader.Read(bytes));
        bytes = LsmfFixture.Entities();
        LsmfFixture.U64(bytes, 16, 1);
        Assert.Throws<SaveFormatException>(() => LsmfReader.Read(bytes));
        bytes = LsmfFixture.Entities();
        var directory = LsmfFixture.Descriptor(bytes) - (int)BinaryData.U32(bytes, 32);
        bytes[directory] = 0xff;
        Assert.Throws<SaveFormatException>(() => LsmfReader.Read(bytes));
        Assert.Throws<SaveFormatException>(() => LsmfReader.Read(LsmfFixture.Create(("duplicate", 1, 1, [1]), ("duplicate", 1, 1, [2]))));
    }

    [Fact]
    public void DescriptorBudgetAndNominalRangeDoNotAllowOversizedAllocations()
    {
        var bytes = LsmfFixture.Entities();
        bytes[36] = 1;
        bytes[37] = 0x10; // 4097
        Assert.Throws<SaveFormatException>(() => LsmfReader.Read(bytes));
        bytes = LsmfFixture.Entities();
        LsmfFixture.U32(bytes, LsmfFixture.Descriptor(bytes) + 24, uint.MaxValue);
        Assert.Throws<SaveFormatException>(() => LsmfReader.Read(bytes));
        bytes = LsmfFixture.Entities();
        LsmfFixture.U64(bytes, LsmfFixture.Descriptor(bytes) + 32, 1_000_000);
        Assert.Throws<SaveFormatException>(() => LsmfReader.Read(bytes));
        Assert.Throws<SaveFormatException>(() => LsmfReader.Read(LsmfFixture.Create(("nonempty", 0, 1, []))));
        Assert.Throws<SaveFormatException>(() => LsmfReader.Read(LsmfFixture.Create(("core.v0.EntityId", 8, 1, new byte[8]))).Entities());
    }

    [Fact]
    public void EveryTruncationOfTheDirectoryIsRejected()
    {
        var bytes = LsmfFixture.Entities();
        var directoryEnd = bytes.Length - 3;
        for (var length = 48; length < directoryEnd; length++)
            Assert.Throws<SaveFormatException>(() => LsmfReader.Read(bytes.AsMemory(0, length)));
    }
}
