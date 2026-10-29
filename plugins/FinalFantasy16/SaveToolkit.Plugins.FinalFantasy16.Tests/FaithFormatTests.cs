using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Binary;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithFormatTests
{
    private readonly FinalFantasy16Plugin _plugin = new();

    [Fact]
    public void CrcMatchesStandardVectors()
    {
        Assert.Equal(0U, Crc32Ieee.Compute([]));
        Assert.Equal(0xCBF43926U, Crc32Ieee.Compute("123456789"u8));
        Assert.Equal(0xD202EF8DU, Crc32Ieee.Compute([0]));
        Assert.Equal(0x29058C73U, Crc32Ieee.Compute(Enumerable.Range(0, 256).Select(value => (byte)value).ToArray()));
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("crc")]
    [InlineData("header-size")]
    [InlineData("version")]
    [InlineData("small-block")]
    [InlineData("unaligned-block")]
    [InlineData("oversized-block")]
    [InlineData("missing-terminator")]
    [InlineData("truncated")]
    public void CorruptionIsRejectedWithSaveFormatException(string corruption)
    {
        var bytes = FaithTestData.Container((99, 1, new byte[8]));
        switch (corruption)
        {
            case "payload": bytes[32] ^= 1; break;
            case "crc": bytes[4] ^= 1; break;
            case "header-size": bytes[0] = 8; break;
            case "version": bytes[8] ^= 1; break;
            case "small-block": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 16); break;
            case "unaligned-block": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 25); break;
            case "oversized-block": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 0xFFFFFFF8); break;
            case "missing-terminator": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - 4), 99); break;
            case "truncated": bytes = bytes[..^1]; break;
            default: throw new ArgumentException("Unknown corruption.", nameof(corruption));
        }

        if (corruption is not ("payload" or "crc"))
        {
            FaithTestData.FixCrc(bytes); // Structural validation must reject even a valid checksum.
        }

        Assert.Throws<SaveFormatException>(() => _plugin.Open(FaithTestData.File(bytes)));
    }

    [Fact]
    public void ShortInputsAndForeignFilesAreNotClaimed()
    {
        for (var length = 0; length < FaithContainer.HeaderSize; length++)
        {
            var file = FaithTestData.File(new byte[length]);
            Assert.False(_plugin.Probe(file).IsMatch);
            Assert.Throws<SaveFormatException>(() => _plugin.Open(file));
        }

        var valid = FaithTestData.Container((99, 1, new byte[8]));
        Assert.False(_plugin.Probe(FaithTestData.File(valid, "PPSA01288")).IsMatch);
        Assert.False(_plugin.Probe(FaithTestData.File(valid, name: "game-save-data10")).IsMatch);
        Assert.False(_plugin.Probe(FaithTestData.File(valid, name: "param.sfo")).IsMatch);
        Assert.Throws<SaveFormatException>(() => _plugin.Open(FaithTestData.File(valid, name: "system-save-data")));
    }

    [Fact]
    public void UnknownBlocksDuplicateTypesAndTrailingBytesArePreserved()
    {
        var bytes = FaithTestData.Container((99, 1, new byte[8]), (99, 2, new byte[16]));
        bytes = [.. bytes, 0x12, 0x34, 0x56];
        FaithTestData.FixCrc(bytes);
        var parsed = FaithContainer.Parse(bytes);
        Assert.Equal(2, parsed.Blocks.Count);
        Assert.Equal(7, bytes.Length - parsed.TrailerOffset);
        using var document = _plugin.Open(FaithTestData.File(bytes));
        Assert.False(document.CanEdit);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("initialized")]
    [InlineData("next")]
    [InlineData("size")]
    [InlineData("secondary")]
    public void InvalidKnownAllocatorBoundsAreRejected(string field)
    {
        var payload = FaithTestData.Items();
        const int Metadata = FaithInventory.Capacity * 20;
        var offset = field switch
        {
            "initialized" => Metadata,
            "next" => Metadata + 8,
            "size" => Metadata + 16 + FaithInventory.Capacity * 8,
            "secondary" => 33624 + 100 * 20,
            _ => throw new ArgumentException("Unknown allocator field.", nameof(field)),
        };
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset), 1201);
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, payload));
        Assert.Throws<SaveFormatException>(() => _plugin.Open(FaithTestData.File(bytes)));
    }

    [Fact]
    public void UnsubstantiatedAllocationAndSchemaVersionsStayReadOnly()
    {
        var payload = FaithTestData.Items();
        const int FirstKey = FaithInventory.Capacity * 20 + 16;
        payload[FirstKey] = 2;
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, payload));
        using var unfamiliar = _plugin.Open(FaithTestData.File(bytes));
        Assert.False(unfamiliar.CanEdit);
        Assert.Equal(bytes, Assert.Single(unfamiliar.Serialize()).Content.ToArray());

        bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion + 1, FaithTestData.Items()));
        using var version = _plugin.Open(FaithTestData.File(bytes));
        Assert.False(version.CanEdit);
        Assert.Equal(bytes, Assert.Single(version.Serialize()).Content.ToArray());
    }

    [Fact]
    public void DemoDoesNotSilentlyUseExtendedRetailPlayerSchema()
    {
        var bytes = FaithTestData.Container((FaithSchema.PlayerType, FaithSchema.PlayerVersion, new byte[FaithSchema.ExtendedPlayerLength]));
        using var retail = _plugin.Open(FaithTestData.File(bytes));
        using var demo = _plugin.Open(FaithTestData.File(bytes, "PPSA13803"));
        _ = retail.BuildView();
        _ = demo.BuildView();
        Assert.Equal(0, retail.GetValue("player/level").AsInt32());
        Assert.Throws<KeyNotFoundException>(() => demo.GetValue("player/level"));
        Assert.Equal(bytes, Assert.Single(demo.Serialize()).Content.ToArray());
    }
}
