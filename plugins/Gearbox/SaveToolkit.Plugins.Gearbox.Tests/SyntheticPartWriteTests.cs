using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class SyntheticPartWriteTests
{
    private static readonly int[] Seeds = [0, -1, int.MinValue, int.MaxValue];
    private const int OriginalIndex = 1;
    private const int ReplacementIndex = 2;

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void ExistingRegularAndGenericSlots_WriteExactRequestedBitsAndRepairCrc(bool wonderlands, int slotIndex)
    {
        var version = wonderlands ? 5 : 4;
        foreach (var seed in Seeds)
        {
            var payload = SyntheticItem.Payload(wonderlands, partIndex: OriginalIndex, genericPartIndex: OriginalIndex);
            var serial = IndependentSerialProbe.Encode(payload, version, seed);
            var original = serial.ToArray();
            var reader = new SerialReader(wonderlands);
            var info = reader.Read(serial);
            Assert.True(info.Complete, info.Diagnostic);
            Assert.Single(info.Parts);
            Assert.Single(info.GenericParts);
            var slot = SerialParts.Slot(info, slotIndex);
            var candidate = new SerialAsset(slot.Category, ReplacementIndex, null).Key;

            var changed = SerialParts.Set(serial, wonderlands, slotIndex, candidate);

            // Rebuild the expected syntax independently rather than patching production offsets.
            var expected = SyntheticItem.Payload(wonderlands,
                partIndex: slotIndex == 0 ? ReplacementIndex : OriginalIndex,
                genericPartIndex: slotIndex == 1 ? ReplacementIndex : OriginalIndex);
            Assert.Equal(expected, IndependentSerialProbe.Decode(changed));
            Assert.Equal(IndependentSerialProbe.Encode(expected, version, seed), changed);
            Assert.Equal(original[..5], changed[..5]);
            Assert.NotEqual(original, changed);
            Assert.Equal(original, serial);
            var reopened = reader.Read(changed);
            Assert.True(reopened.Complete, reopened.Diagnostic);
            Assert.Equal(candidate, SerialParts.Slot(reopened, slotIndex).Key);
            Assert.Equal(original, SerialParts.Set(changed, wonderlands, slotIndex, slot.Key));
            Assert.Equal(original, SerialParts.Set(serial, wonderlands, slotIndex, slot.Key));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidSlotCategoryIndexOrCrc_RejectsWithoutMutatingInput(bool wonderlands)
    {
        var serial = IndependentSerialProbe.Encode(
            SyntheticItem.Payload(wonderlands, partIndex: OriginalIndex, genericPartIndex: OriginalIndex),
            wonderlands ? 5 : 4, int.MinValue);
        var original = serial.ToArray();
        var info = new SerialReader(wonderlands).Read(serial);
        Assert.True(info.Complete, info.Diagnostic);
        var slot = SerialParts.Slot(info, 0);
        foreach (var (index, key) in new[]
        {
            (-1, slot.Key), (2, slot.Key), (int.MaxValue, slot.Key),
            (0, "ManufacturerData:1"), (0, slot.Category + ":0"),
            (0, slot.Category + ":2147483647"), (0, "missing:99"),
        })
        {
            Assert.Throws<SaveFormatException>(() => SerialParts.Set(serial, wonderlands, index, key));
            Assert.Equal(original, serial);
        }

        var corrupt = serial.ToArray();
        corrupt[^1] ^= 1;
        var corruptBefore = corrupt.ToArray();
        Assert.Throws<SaveFormatException>(() => SerialParts.Set(corrupt, wonderlands, 0, slot.Key));
        Assert.Equal(corruptBefore, corrupt);
        Assert.Equal(original, serial);
    }
}
