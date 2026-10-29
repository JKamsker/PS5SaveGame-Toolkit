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
        var files = Files.Where(file => Word(file.ReadAllBytes(), 0) != 0).ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), end);
            var error = Assert.Throws<SaveFormatException>(() => new SuckerPunchPlugin().Open(WithBytes(bytes)));
            Assert.Contains("content end", error.Message);
        }
    }

    [Theory]
    [InlineData("inventory-name-length", "Invalid Yōtei record name length.")]
    [InlineData("inventory-name-byte", "Invalid Yōtei record identifier.")]
    [InlineData("inventory-duplicate", "Duplicate Yōtei record identifier.")]
    [InlineData("third-inventory-anchor", "Yōtei inventory table is ambiguous.")]
    [InlineData("secondary-name", "Unrecognised Yōtei inventory layout.")]
    [InlineData("gear-count", "Yōtei gear registry is missing or ambiguous.")]
    [InlineData("gear-name-length", "Invalid Yōtei record name length.")]
    [InlineData("statistics-count", "Yōtei statistics table is missing or ambiguous.")]
    [InlineData("statistics-name-length", "Invalid Yōtei record name length.")]
    [InlineData("secondary-name-length", "Invalid Yōtei record name length.")]
    [InlineData("truncated-content", "Yōtei inventory table is missing.")]
    public void MalformedKnownStructuresRejectAfterAnIndependentlyRepairedChecksum(string mutation, string expected)
    {
        var files = Files.Where(file => Word(file.ReadAllBytes(), 0) != 0).ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files) AssertMalformedStructures(file.ReadAllBytes(), mutation, expected);
    }

    private static void AssertMalformedStructures(byte[] bytes, string mutation, string expected)
    {
        ReadOnlySpan<byte> inventoryAnchor = "\x30\0\0\0\x0b\0\0\0ArrowNormal"u8;
        var first = bytes.AsSpan().IndexOf(inventoryAnchor);
        var second = first + inventoryAnchor.Length + bytes.AsSpan(first + inventoryAnchor.Length).IndexOf(inventoryAnchor);
        var gear = bytes.AsSpan().IndexOf("\x0d\x04\0\0\x0b\0\0\0HalfShog101"u8);
        var stats = bytes.AsSpan().IndexOf("\x21\0\0\0\x0b\0\0\0FishSpeared"u8);
        var monName = InventoryRecords(bytes).Single(row => row.Name == "Mon").Data - "Mon".Length;
        switch (mutation)
        {
            case "inventory-name-length": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(monName - sizeof(uint)), uint.MaxValue); break;
            case "inventory-name-byte": bytes[monName] = 0xFF; break;
            case "inventory-duplicate":
                var record = InventoryRecords(bytes).Single(row => row.Name == "ArrowDisarm");
                "ArrowNormal"u8.CopyTo(bytes.AsSpan(record.Data - "ArrowDisarm".Length)); break;
            case "third-inventory-anchor": inventoryAnchor.CopyTo(bytes.AsSpan(256)); break;
            case "secondary-name":
                var secondaryMon = ReadTable(bytes, second, 48, 16).Single(row => row.Name == "Mon");
                bytes[secondaryMon.Data - secondaryMon.Name.Length] = (byte)'X'; break;
            case "gear-count": bytes[gear] = 0; break;
            case "gear-name-length":
                var gearRow = ReadTable(bytes, gear, 1037, 2)[1];
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(gearRow.Data - gearRow.Name.Length - sizeof(uint)), 0); break;
            case "statistics-count": bytes[stats] = 34; break;
            case "statistics-name-length":
                var statsRow = ReadTable(bytes, stats, 33, sizeof(uint))[1];
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(statsRow.Data - statsRow.Name.Length - sizeof(uint)), uint.MaxValue); break;
            case "secondary-name-length":
                var secondaryRow = ReadTable(bytes, second, 48, 16)[1];
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secondaryRow.Data - secondaryRow.Name.Length - sizeof(uint)), uint.MaxValue); break;
            case "truncated-content": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)(first + 8)); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        Repair(bytes);
        var error = Assert.Throws<SaveFormatException>(() => new SuckerPunchPlugin().Open(WithBytes(bytes)));
        Assert.Equal(expected, error.Message);
    }

    [Fact]
    public void EveryCurrentSampleRejectsPhysicalTruncationAndNonemptyZeroMagicPayloads()
    {
        Assert.NotEmpty(Files);
        foreach (var file in Files)
        {
            var bytes = file.ReadAllBytes();
            foreach (var end in new[] { 0, 15, (int)Word(bytes, 8), bytes.Length - 1 }.Distinct())
                Assert.Throws<SaveFormatException>(() => new SuckerPunchPlugin().Open(WithBytes(bytes[..end])));
            // Magic zero is permitted only for the complete all-zero placeholder.
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0);
            bytes[^1] = 1;
            Assert.Equal("Unrecognised Sucker Punch save magic.",
                Assert.Throws<SaveFormatException>(() => new SuckerPunchPlugin().Open(WithBytes(bytes))).Message);
        }
    }

    [Fact]
    public void ImportedFileAndExportBuffersAndBlobPagesRemainIsolated()
    {
        var bytes = Slot("suckerpunchUser4").ReadAllBytes();
        var original = bytes.ToArray();
        var file = WithBytes(bytes);
        using var doc = Open(file);
        bytes[0] = 0;
        WriteAssertions.FlipReturnedByte(Assert.Single(doc.Serialize()).Content);
        Assert.Equal(original, Assert.Single(doc.Serialize()).Content.ToArray());
        WriteAssertions.FlipReturnedByte(doc.ReadBlob("content", 7, 32));
        Assert.Equal(original, Assert.Single(doc.Serialize()).Content.ToArray());
        Assert.Equal(original.AsSpan(7, 32).ToArray(), doc.ReadBlob("content", 7, 32).ToArray());
        Assert.Equal(original, file.ReadAllBytes());
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.ReadBlob("content", -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.ReadBlob("content", long.MaxValue, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.ReadBlob("content", 0, -1));
        Assert.Throws<KeyNotFoundException>(() => doc.ReadBlob("unknown", 0, 1));
    }
}
