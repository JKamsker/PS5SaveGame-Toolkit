using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class HeaderRobustnessTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void EveryIncompleteContainerPrefix_IsRejected(bool wonderlands, bool profile, bool unicode)
    {
        var bytes = ContainerTests.Create(wonderlands, profile, unicode);
        var title = wonderlands ? "PPSA01493" : "PPSA01463";
        for (var length = 0; length < bytes.Length; length++)
        {
            var prefix = bytes[..length];
            var error = Assert.Throws<SaveFormatException>(() => GearboxContainer.Parse(prefix, title, profile).Decode());
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
        }
        var container = GearboxContainer.Parse(bytes, title, profile);
        Assert.Equal(bytes, container.Encode(container.Decode()));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(1025)]
    [InlineData(int.MaxValue)]
    public void CustomVersionCount_IsBoundedBeforeReadingRecords(int count)
    {
        var bytes = ContainerTests.Create(false, false);
        var countOffset = CountOffset(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(countOffset), count);
        var error = Assert.Throws<SaveFormatException>(() => GearboxContainer.Parse(bytes, "PPSA01463", false));
        Assert.Contains("custom-version count", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1024)]
    public void CustomVersionTable_ExactSupportedBoundariesAreLossless(int count)
    {
        const int recordSize = 20;
        var original = ContainerTests.Create(false, false);
        var offset = CountOffset(original);
        byte[] bytes = [.. original[..offset], .. new byte[sizeof(int)],
            .. new byte[count * recordSize], .. original[(offset + sizeof(int) + recordSize)..]];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), count);
        var container = GearboxContainer.Parse(bytes, "PPSA01463", false);
        Assert.Equal(bytes, container.Encode(container.Decode()));
        // A table with the same count but no final record must fail at the header boundary.
        if (count > 0)
        {
            var truncated = bytes[..(offset + sizeof(int) + count * recordSize - 1)];
            Assert.Throws<SaveFormatException>(() => GearboxContainer.Parse(truncated, "PPSA01463", false));
        }
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public void InvalidPayloadLength_IsRejected(int length)
    {
        var bytes = ContainerTests.Create(false, false);
        var offset = GearboxContainer.Parse(bytes, "PPSA01463", false).PayloadOffset;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset - sizeof(int)), length);
        Assert.Throws<SaveFormatException>(() => GearboxContainer.Parse(bytes, "PPSA01463", false));
    }

    [Theory]
    [InlineData("00000080", "Invalid GVAS string length.")] // Int32.MinValue cannot be negated.
    [InlineData("01400000", "Invalid GVAS string length.")] // UTF-8 over the header string limit.
    [InlineData("FFDFFFFF", "Invalid GVAS string length.")] // UTF-16 over the header string limit.
    [InlineData("02000000FF00", "Invalid GVAS string encoding.")] // Invalid UTF-8.
    [InlineData("FEFFFFFF00D80000", "Invalid GVAS string encoding.")] // Unpaired UTF-16 surrogate.
    [InlineData("0100000001", "GVAS string is not terminated.")] // Missing UTF-8 NUL.
    [InlineData("FFFFFFFF0001", "GVAS string is not terminated.")] // Missing UTF-16 NUL.
    public void CorruptHeaderStrings_RejectWithDocumentedException(string hex, string expectedMessage)
    {
        var bytes = Convert.FromHexString(hex);
        Assert.Equal(expectedMessage, Assert.Throws<SaveFormatException>(() => ReadString(bytes)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CipherPartialWindows_HaveNoPaddingOrImplicitZeroTrimming(bool profile)
    {
        for (var length = 0; length <= 97; length++)
        {
            var plain = new byte[length];
            var cipher = BogoCrypt.Encode(plain, profile);
            Assert.Equal(length, cipher.Length);
            Assert.Equal(plain, BogoCrypt.Decode(cipher, profile));
        }
    }

    private static string ReadString(byte[] bytes)
    {
        var reader = new GvasReader(bytes);
        return reader.String();
    }

    private static int CountOffset(byte[] bytes)
    {
        var reader = new GvasReader(bytes);
        _ = reader.Take(22); // Magic, versions and engine build.
        _ = reader.String();
        _ = reader.Int32(); // Custom-version format.
        return reader.Position;
    }
}
