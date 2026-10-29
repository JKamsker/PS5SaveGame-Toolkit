using System.Buffers.Binary;
using SaveToolkit.Sdk.Binary;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithCrcTests
{
    // Pinned before replacing FaithCrc32, independently checked with Python zlib.crc32.
    // Only hash expectations are stored here; private sample bytes stay outside the checkout.
    [Theory]
    [InlineData("PPSA10665", "FAITHDATAAUTO", "game-save-data00", 0, 0xCD87931AU)]
    [InlineData("PPSA10665", "FAITHDATAAUTO", "game-save-data00", 1, 0x7A4934F1U)]
    [InlineData("PPSA10665", "FAITHDATASYSTEM", "system-save-data", 0, 0x32739F6EU)]
    [InlineData("PPSA13803", "FAITHDATAAUTO", "game-save-data00", 0, 0x0AFA3619U)]
    [InlineData("PPSA13803", "FAITHDATASYSTEM", "system-save-data", 0, 0xF81298C4U)]
    public void SampleHashesIncludeTheTerminatorAndUseLittleEndianStorage(
        string title, string directory, string name, int occurrence, uint expected)
    {
        var bytes = FaithSamples.Snapshot(title, directory, name, occurrence).ReadAllBytes();
        Assert.Equal(expected, Crc32Ieee.Compute(bytes.AsSpan(FaithContainer.HeaderSize)));
        Assert.Equal(expected, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(FaithContainer.CrcOffset)));
        Assert.NotEqual(expected, Crc32Ieee.Compute(
            bytes.AsSpan(FaithContainer.HeaderSize, bytes.Length - FaithContainer.HeaderSize - sizeof(uint))));

        // Header values are excluded; the four-byte terminator is included in the covered body.
        bytes[FaithContainer.VersionOffset] ^= 1;
        Assert.Equal(expected, Crc32Ieee.Compute(bytes.AsSpan(FaithContainer.HeaderSize)));
        bytes[^1] ^= 1;
        Assert.NotEqual(expected, Crc32Ieee.Compute(bytes.AsSpan(FaithContainer.HeaderSize)));
    }
}
