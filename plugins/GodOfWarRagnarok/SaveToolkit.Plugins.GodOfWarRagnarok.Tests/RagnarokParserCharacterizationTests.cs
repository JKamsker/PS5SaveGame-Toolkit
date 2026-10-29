using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokParserCharacterizationTests
{
    [Theory]
    // Captured from the merged implementation before extracting parser phases.
    [InlineData(false, "2A502D3B6E4299C2FA6D7C5D117B4B1E01BB3E9AC1381694CE491FAF8FE8DC7C")]
    [InlineData(true, "DA342C760A81B6F0BCA50030C2F4454C62F88CDE57D5A30565F52BE6CC4647DC")]
    public void CompleteIndexAndGrammarEvidenceRemainUnchanged(bool nested, string expected)
    {
        var bytes = nested ? new RagnarokNestedFixture().Create() : RagnarokTestSupport.LegacyGameplay();
        var index = RagnarokRecordIndex.Parse(bytes, false);
        var json = JsonSerializer.Serialize(index);
        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        Assert.True(expected == actual, actual);
    }

    [Theory]
    [InlineData("directory-before-text", "Ragnarök directory IDs are not strictly increasing.")]
    [InlineData("secondary-before-wallet", "A secondary Ragnarök ID is missing from the directory.")]
    [InlineData("wallet-before-text", "Expected exactly one bounded Ragnarök hero-wallet record.")]
    [InlineData("wallet-range-before-text", "Invalid or truncated record array at 0x514.")]
    [InlineData("text-before-nested", "The Ragnarök location description is not valid UTF-8.")]
    public void OuterParserRetainsDiagnosticPrecedence(string fault, string expected)
    {
        var bytes = new RagnarokNestedFixture().Create();
        switch (fault)
        {
            case "directory-before-text":
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x44E), 0);
                bytes[0x50] = 0xFF;
                break;
            case "secondary-before-wallet":
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x462), 99);
                bytes.AsSpan(0x500, 8).Clear();
                break;
            case "wallet-before-text":
                bytes.AsSpan(0x500, 8).Clear();
                bytes[0x50] = 0xFF;
                break;
            case "wallet-range-before-text":
                Write(bytes, 0x508, uint.MaxValue);
                bytes[0x50] = 0xFF;
                break;
            case "text-before-nested":
                bytes[0x50] = 0xFF;
                Write(bytes, 0x4FC, uint.MaxValue);
                break;
            default: throw new ArgumentException(fault);
        }

        Assert.Equal(expected, Assert.Throws<SaveFormatException>(() => RagnarokRecordIndex.Parse(bytes, false)).Message);
    }

    [Theory]
    [InlineData("wallets-before-links", "Duplicate or excessive nested Ragnarök wallet identities.")]
    [InlineData("links-before-flags", "Nested Ragnarök link count does not consume its declared byte length.")]
    [InlineData("prefix-before-group-count", "Unsupported nested Ragnarök flag-group prefix.")]
    [InlineData("group-count-before-rows", "Excessive nested Ragnarök flag-group count.")]
    [InlineData("group-identity-before-row-count", "Duplicate nested Ragnarök flag-group identity.")]
    public void NestedParserRetainsDiagnosticPrecedence(string fault, string expected)
    {
        var fixture = new RagnarokNestedFixture();
        var bytes = fixture.Create();
        switch (fault)
        {
            case "wallets-before-links":
                bytes.AsSpan(fixture.WalletOffset(1), 8).CopyTo(bytes.AsSpan(fixture.WalletOffset(2)));
                Write(bytes, fixture.WalletBlockEnd + 2, 2);
                break;
            case "links-before-flags":
                Write(bytes, fixture.WalletBlockEnd + 2, 2);
                bytes[fixture.FlagsPayloadOffset] ^= 1;
                break;
            case "prefix-before-group-count":
                bytes[fixture.FlagsPayloadOffset] ^= 1;
                Write(bytes, fixture.FlagsPayloadOffset + 12, uint.MaxValue);
                break;
            case "group-count-before-rows":
                Write(bytes, fixture.FlagsPayloadOffset + 12, 257);
                Write(bytes, fixture.GroupOffset(0) + 8, uint.MaxValue);
                break;
            case "group-identity-before-row-count":
                bytes.AsSpan(fixture.GroupOffset(0), 8).CopyTo(bytes.AsSpan(fixture.GroupOffset(1)));
                Write(bytes, fixture.GroupOffset(1) + 8, uint.MaxValue);
                break;
            default: throw new ArgumentException(fault);
        }

        Assert.Equal(expected, Assert.Throws<SaveFormatException>(() => RagnarokRecordIndex.Parse(bytes, false)).Message);
    }

    private static void Write(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
