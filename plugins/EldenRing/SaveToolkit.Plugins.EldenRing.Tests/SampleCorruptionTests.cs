using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class SampleCorruptionTests
{
    [Fact]
    public void SampleTruncationsAtEverySlotAndAllocationBoundaryFailBeforeReadingCharacters()
    {
        var bytes = WriteTestSupport.Sample().ReadAllBytes();
        var boundaries = Enumerable.Range(0, EldenRingLayout.SlotCount + 1)
            .Select(index => EldenRingLayout.HeaderSize + index * EldenRingLayout.SlotSize)
            .Concat([0, EldenRingLayout.RegulationOffset, EldenRingLayout.MinimumFileSize]);
        foreach (var boundary in boundaries)
        {
            foreach (var length in new[] { boundary - 1, boundary, boundary + 1 }
                .Where(length => length >= 0 && length < EldenRingLayout.MinimumFileSize))
            {
                var error = Assert.Throws<SaveFormatException>(() => EldenRingParser.Parse(bytes.AsSpan(0, length)));
                Assert.Contains("truncated", error.Message);
            }
        }
        Assert.Single(EldenRingParser.Parse(bytes).Slots, slot => slot.Active);
    }

    [Theory]
    [InlineData("held-common")]
    [InlineData("held-key")]
    [InlineData("storage-common")]
    [InlineData("storage-key")]
    [InlineData("projectiles")]
    [InlineData("regions")]
    [InlineData("acquisition")]
    public void MutatedSampleCountsAreRejectedWithoutModifyingTheSample(string target)
    {
        var bytes = WriteTestSupport.Sample().ReadAllBytes();
        var slot = Assert.Single(EldenRingParser.Parse(bytes).Slots, slot => slot.Active);
        var character = SemanticReader.Read(bytes, slot);
        var held = character.Containers.Single(container => container.Id == "held").Range;
        var storage = character.Containers.Single(container => container.Id == "storage").Range;
        var offset = target switch
        {
            "held-common" => held.Offset,
            "held-key" => held.Offset + sizeof(uint) + SemanticLayout.HeldCommonCapacity * SemanticLayout.InventoryRowSize,
            "storage-common" => storage.Offset,
            "storage-key" => storage.Offset + sizeof(uint) + SemanticLayout.StorageCommonCapacity * SemanticLayout.InventoryRowSize,
            "projectiles" => held.Offset + held.Length + SemanticLayout.MagicSize + SemanticLayout.QuickItemsSize + SemanticLayout.EquippedGesturesSize,
            "regions" => storage.Offset + storage.Length + SemanticLayout.GesturesSize,
            _ => character.Ranges.Single(range => range.Name == SemanticLayout.AcquisitionRangeName).Offset,
        };
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), uint.MaxValue);
        var error = Assert.Throws<SaveFormatException>(() => new EldenRingPlugin().Open(WriteTestSupport.File(bytes)));
        Assert.True(error.Message.Contains("capacity", StringComparison.Ordinal)
            || error.Message.Contains("character slot", StringComparison.Ordinal), error.Message);
        Assert.NotEqual(uint.MaxValue, EldenRingParser.ReadUInt32(WriteTestSupport.Sample().ReadAllBytes(), offset));
    }

    [Fact]
    public void CorruptSampleRegulationRetainsTheDocumentedWarningAndExactOpaqueBytes()
    {
        var bytes = WriteTestSupport.Sample().ReadAllBytes();
        // CBC IV byte 0 changes DCX magic deterministically, leaving all character data alone.
        const int ivOffset = EldenRingLayout.RegulationOffset + 16;
        bytes[ivOffset] ^= 1;
        using var document = new EldenRingPlugin().Open(WriteTestSupport.File(bytes));
        var warning = Assert.IsType<TextNode>(Assert.Single(document.ExpandNode("regulation/metadata")));
        Assert.Equal(Tone.Warning, warning.Tone);
        Assert.Contains("Unsupported regulation DCX header", warning.Text);
        Assert.Contains("preserved", warning.Text);
        Assert.True(bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }
}
