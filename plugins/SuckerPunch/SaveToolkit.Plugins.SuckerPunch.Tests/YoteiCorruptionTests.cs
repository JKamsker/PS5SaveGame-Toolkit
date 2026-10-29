using System.Buffers.Binary;
using SaveToolkit.Sdk;
using static SaveToolkit.Plugins.SuckerPunch.Tests.YoteiTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class YoteiCorruptionTests
{
    [Theory]
    [InlineData(0U)]
    [InlineData(15U)]
    [InlineData(616449U)]
    [InlineData(uint.MaxValue)]
    public void InvalidDeclaredEndsRejectClearlyBeforeReadingTables(uint end)
    {
        var bytes = Slot("suckerpunchUser4").ReadAllBytes();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), end);
        var error = Assert.Throws<SaveFormatException>(() => new SuckerPunchPlugin().Open(WithBytes(bytes)));
        Assert.Contains("content end", error.Message);
    }

    [Theory]
    [InlineData("inventory-name-length")]
    [InlineData("inventory-name-byte")]
    [InlineData("inventory-duplicate")]
    [InlineData("third-inventory-anchor")]
    [InlineData("secondary-name")]
    [InlineData("gear-count")]
    [InlineData("gear-name-length")]
    [InlineData("statistics-count")]
    [InlineData("truncated-content")]
    public void MalformedKnownStructuresRejectAfterAnIndependentlyRepairedChecksum(string mutation)
    {
        var bytes = Slot("suckerpunchUser4").ReadAllBytes();
        ReadOnlySpan<byte> inventoryAnchor = "\x30\0\0\0\x0b\0\0\0ArrowNormal"u8;
        var first = bytes.AsSpan().IndexOf(inventoryAnchor);
        var second = first + inventoryAnchor.Length + bytes.AsSpan(first + inventoryAnchor.Length).IndexOf(inventoryAnchor);
        var gear = bytes.AsSpan().IndexOf("\x0d\x04\0\0\x0b\0\0\0HalfShog101"u8);
        var stats = bytes.AsSpan().IndexOf("\x21\0\0\0\x0b\0\0\0FishSpeared"u8);
        switch (mutation)
        {
            case "inventory-name-length": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(first + 4), uint.MaxValue); break;
            case "inventory-name-byte": bytes[first + 8] = 0xFF; break;
            case "inventory-duplicate":
                var record = InventoryRecords(bytes).Single(row => row.Name == "ArrowDisarm");
                "ArrowNormal"u8.CopyTo(bytes.AsSpan(record.Data - "ArrowDisarm".Length)); break;
            case "third-inventory-anchor": inventoryAnchor.CopyTo(bytes.AsSpan(256)); break;
            case "secondary-name": bytes[second + 8] = (byte)'X'; break;
            case "gear-count": bytes[gear] = 0; break;
            case "gear-name-length": bytes[gear + 4] = 0; break;
            case "statistics-count": bytes[stats] = 34; break;
            case "truncated-content": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)(first + 8)); break;
        }
        Repair(bytes);
        var error = Assert.Throws<SaveFormatException>(() => new SuckerPunchPlugin().Open(WithBytes(bytes)));
        Assert.Contains("Yōtei", error.Message);
    }

    [Fact]
    public void InputAndExportBuffersAndBlobPagesRemainIsolated()
    {
        var bytes = Slot("suckerpunchUser4").ReadAllBytes();
        var original = bytes.ToArray();
        using var doc = Open(WithBytes(bytes));
        bytes[0] = 0;
        var output = Assert.Single(doc.Serialize()).Content.ToArray();
        output[0] = 0;
        var page = doc.ReadBlob("content", 0, 32).ToArray();
        page[0] = 0;
        Assert.Equal(original, Assert.Single(doc.Serialize()).Content.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.ReadBlob("content", -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.ReadBlob("content", long.MaxValue, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.ReadBlob("content", 0, -1));
        Assert.Throws<KeyNotFoundException>(() => doc.ReadBlob("unknown", 0, 1));
    }
}
