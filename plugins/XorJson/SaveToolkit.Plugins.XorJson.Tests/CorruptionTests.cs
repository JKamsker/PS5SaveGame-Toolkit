using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class CorruptionTests
{
    private const int MaximumDepth = 64;
    private const int MaximumValues = 250_000;
    private const int MaximumBytes = 16 * 1024 * 1024;

    public static TheoryData<string> InvalidJson => new()
    {
        "", " ", "{", "[", "{\"x\":}", "[1,]", "{\"x\":1,}", "{}[]", "[1]null",
        "[NaN]", "[Infinity]", "[+1]", "[01]", "[1.]", "[.1]", "[1e]", "[1e+]",
        "[True]", "[undefined]", "[/* comment */1]", "{x:1}", "[\"unterminated]",
        "[\"\\x41\"]", "[\"\\u12\"]", "[\"\\uDC00\"]", "[\"\\uD800x\"]", "[\"\u0001\"]",
        "\uFEFF\uFEFF{}", "[]\0",
    };

    [Theory]
    [MemberData(nameof(InvalidJson))]
    public void InvalidGrammarIsDeclinedAndRejectedForBothTransforms(string text)
    {
        // The object wrapper makes the alternate transform invalid too. Without it,
        // XOR-1 [01] also decodes as valid byte-minus-one [21], so rejection is wrong.
        foreach (var xor in new[] { false, true }) AssertRejected(XorJsonTests.Encode("{\"v\":" + text + "}", xor));
    }

    public static TheoryData<byte[]> InvalidUtf8 => new()
    {
        new byte[] { 0x80 }, new byte[] { 0xC0, 0xAF }, new byte[] { 0xC2 },
        new byte[] { 0xED, 0xA0, 0x80 }, new byte[] { 0xF4, 0x90, 0x80, 0x80 },
        new byte[] { 0xF0, 0x9F, 0x98 }, new byte[] { 0xFF },
    };

    [Theory]
    [MemberData(nameof(InvalidUtf8))]
    public void MalformedUtf8InsideStringsCannotBeDecodedWithReplacementCharacters(byte[] invalid)
    {
        var plain = "{\"s\":\""u8.ToArray().Concat(invalid).Concat("\"}"u8.ToArray()).ToArray();
        foreach (var xor in new[] { false, true })
            AssertRejected(plain.Select(value => xor ? (byte)(value ^ 1) : unchecked((byte)(value - 1))).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryTruncatedPrefixOfACompleteDocumentIsRejected(bool xor)
    {
        var complete = XorJsonTests.Encode("{\"text\":\"日本語😀\",\"number\":-1.2300e+02,\"rows\":[true,null,{}]}", xor);
        for (var length = 0; length < complete.Length; length++) AssertRejected(complete[..length]);
        AssertAccepted(complete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaximumDepthIsAcceptedAndTheNextContainerIsRejected(bool xor)
    {
        // Include the object in the depth count and disambiguate the transforms.
        AssertAccepted(XorJsonTests.Encode("{\"v\":" + new string('[', MaximumDepth - 1) + "0" + new string(']', MaximumDepth - 1) + "}", xor));
        AssertRejected(XorJsonTests.Encode("{\"v\":" + new string('[', MaximumDepth) + "0" + new string(']', MaximumDepth) + "}", xor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValueLimitCountsTheRootAndRejectsOneAdditionalScalar(bool xor)
    {
        var accepted = "[" + string.Join(',', Enumerable.Repeat("0", MaximumValues - 1)) + "]";
        AssertAccepted(XorJsonTests.Encode(accepted, xor));
        AssertRejected(XorJsonTests.Encode(accepted[..^1] + ",0]", xor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ByteLimitIncludesTrailingWhitespace(bool xor)
    {
        const string json = "{\"s\":\"ok\"}";
        var accepted = json + new string(' ', MaximumBytes - Encoding.UTF8.GetByteCount(json));
        AssertAccepted(XorJsonTests.Encode(accepted, xor));
        AssertRejected(XorJsonTests.Encode(accepted + " ", xor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidSurrogatePairsBomAndEscapedKeysRoundTripWithoutNormalization(bool xor)
    {
        const string text = "\uFEFF{ \"\\uD83D\\uDE00\":\"\\uD83D\\uDE00\",\"literal\":\"😀\",\"escaped\":\"\\u0000\" }\r\n";
        var bytes = XorJsonTests.Encode(text, xor);
        using var document = XorJsonTests.Open(bytes);
        var fields = XorJsonTests.Fields(document);
        Assert.Equal("😀", fields[0].Label);
        Assert.Equal("😀", fields[0].Value.AsString());
        Assert.Equal(fields[0].Value, fields[1].Value);
        Assert.Equal("\0", fields[2].Value.AsString());
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    private static void AssertAccepted(byte[] bytes)
    {
        var file = MemorySaveSet.SingleFile("synthetic.sav", bytes);
        var plugin = new XorJsonPlugin();
        Assert.True(plugin.Probe(file).IsMatch);
        using var document = plugin.Open(file);
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    private static void AssertRejected(byte[] bytes)
    {
        var file = MemorySaveSet.SingleFile("synthetic.sav", bytes);
        var plugin = new XorJsonPlugin();
        Assert.False(plugin.Probe(file).IsMatch);
        Assert.Throws<SaveFormatException>(() => plugin.Open(file));
    }
}
