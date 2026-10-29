using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithParsingCharacterizationTests
{
    [Theory]
    [InlineData("header", "Invalid Faith header size or truncated save.")]
    [InlineData("version", "Unsupported Faith container version (expected plaintext PS5 data).")]
    [InlineData("crc", "Faith CRC32 mismatch: the save is damaged or still wrapped/encrypted.")]
    [InlineData("first-block", "Invalid Faith block size at 0x10.")]
    [InlineData("second-block", "Invalid Faith block size at 0x28.")]
    public void MultipleFaultsRetainDiagnosticPrecedence(string fault, string expected)
    {
        var bytes = FaithTestData.Container((99, 1, new byte[8]), (100, 2, new byte[8]));
        // Later faults must never displace the first diagnostic.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), uint.MaxValue);
        if (fault != "second-block") BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 16);
        FaithTestData.FixCrc(bytes);
        if (fault is "header" or "version" or "crc") bytes[4] ^= 1;
        if (fault is "header" or "version") bytes[8] ^= 1;
        if (fault == "header") bytes[0] = 8;

        var error = Assert.Throws<SaveFormatException>(() => FaithContainer.Parse(bytes));
        Assert.Equal(expected, error.Message);
    }
}
