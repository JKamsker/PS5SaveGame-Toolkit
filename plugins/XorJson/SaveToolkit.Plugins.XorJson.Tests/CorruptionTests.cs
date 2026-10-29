using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class CorruptionTests
{
    private const int MaximumDepth = 64;
    private const int MaximumValues = 250_000;
    private const int MaximumBytes = 16 * 1024 * 1024;
    private const string CharacterPrefix = "{\"SerializeMeta\":{\"HardFormat\":\"FCharacterSaveV1\"},\"CharacterSaveV1\":";
    public enum InputEncoding { Plain, ByteMinusOne, XorOne }

    [Fact]
    public void EveryZeroOneAndTwoByteInputIsDeclinedWithoutLeakingParserExceptions()
    {
        // Empty containers have identical candidate bytes and must reject as ambiguous.
        // No supported, unambiguous document can fit in two bytes.
        AssertRejected([]);
        for (var first = 0; first <= byte.MaxValue; first++)
        {
            AssertRejected([(byte)first]);
            for (var second = 0; second <= byte.MaxValue; second++)
                AssertRejected([(byte)first, (byte)second]);
        }
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("123456789012345678901234567890")]
    [InlineData("\"text\"")]
    [InlineData("\uFEFF")]
    [InlineData("\uFEFF \t\r\n")]
    public void NonContainerPayloadsDoNotInventAnEncodingKey(string text)
    {
        var plain = Encoding.UTF8.GetBytes(text);
        foreach (var mode in Enum.GetValues<InputEncoding>()) AssertRejected(Transform(plain, mode));
    }

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
    public void InvalidGrammarIsDeclinedAndRejectedForEveryEncoding(string text)
    {
        // The object wrapper makes the alternate transform invalid too. Without it,
        // XOR-1 [01] also decodes as valid byte-minus-one [21], so rejection is wrong.
        var plain = Encoding.UTF8.GetBytes(CharacterPrefix + "{\"v\":" + text + "}}");
        foreach (var mode in Enum.GetValues<InputEncoding>()) AssertRejected(Transform(plain, mode));
    }

    public static TheoryData<byte[]> InvalidUtf8 => new()
    {
        new byte[] { 0x80 }, new byte[] { 0xC0, 0xAF }, new byte[] { 0xC2 },
        new byte[] { 0xED, 0xA0, 0x80 }, new byte[] { 0xF4, 0x90, 0x80, 0x80 },
        new byte[] { 0xF0, 0x9F, 0x98 }, new byte[] { 0xFF },
    };

    [Theory]
    [MemberData(nameof(InvalidUtf8))]
    public void MalformedUtf8InKeysStringsAndValuesCannotBeDecodedWithReplacementCharacters(byte[] invalid)
    {
        foreach (var (before, after) in new[] { ("{\"s\":\"", "\"}"), ("{\"", "\":1}"), ("{\"s\":", "}") })
        {
            var plain = Encoding.UTF8.GetBytes(CharacterPrefix + before).Concat(invalid)
                .Concat(Encoding.UTF8.GetBytes(after + "}")).ToArray();
            foreach (var mode in Enum.GetValues<InputEncoding>()) AssertRejected(Transform(plain, mode));
        }
    }

    [Theory]
    [InlineData(InputEncoding.Plain)]
    [InlineData(InputEncoding.ByteMinusOne)]
    [InlineData(InputEncoding.XorOne)]
    public void EveryTruncatedPrefixOfACompleteDocumentIsRejected(InputEncoding mode)
    {
        var plain = Encoding.UTF8.GetBytes("\uFEFF" + CharacterPrefix + "{\"text\":\"日本語😀\",\"number\":-1.2300e+02,\"rows\":[true,null,{}]}}");
        var complete = Transform(plain, mode);
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
    [InlineData(InputEncoding.Plain, false)]
    [InlineData(InputEncoding.ByteMinusOne, false)]
    [InlineData(InputEncoding.XorOne, false)]
    [InlineData(InputEncoding.Plain, true)]
    [InlineData(InputEncoding.ByteMinusOne, true)]
    [InlineData(InputEncoding.XorOne, true)]
    public void ExtremelyDeepCompleteAndTruncatedContainersAreRejected(InputEncoding mode, bool objects)
    {
        const int excessiveDepth = 100_000;
        var opening = string.Concat(Enumerable.Repeat(objects ? "{\"x\":" : "[", excessiveDepth));
        var closing = new string(objects ? '}' : ']', excessiveDepth);
        foreach (var tail in new[] { "", "0", "0" + closing })
            AssertRejected(Transform(Encoding.UTF8.GetBytes(CharacterPrefix + opening + tail + "}"), mode));
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
        var decoder = new HookSaveDecoder(plugin.Decoders.Single());
        Assert.NotEmpty(decoder.Decode(file, new DecodeLimits { MaxValues = 1 }).Values);
    }

    private static void AssertRejected(byte[] bytes)
    {
        var file = MemorySaveSet.SingleFile("synthetic.sav", bytes);
        var plugin = new XorJsonPlugin();
        Assert.False(plugin.Probe(file).IsMatch);
        var error = Assert.Throws<SaveFormatException>(() => plugin.Open(file));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        // The production decoder must share the same rejection, before producing values.
        var decoder = new HookSaveDecoder(plugin.Decoders.Single());
        var decoderError = Assert.Throws<SaveFormatException>(() => decoder.Decode(file, new DecodeLimits()));
        Assert.False(string.IsNullOrWhiteSpace(decoderError.Message));
    }

    private static byte[] Transform(byte[] plain, InputEncoding mode) => mode == InputEncoding.Plain ? plain :
        plain.Select(value => mode == InputEncoding.ByteMinusOne ? unchecked((byte)(value - 1)) : (byte)(value ^ 1)).ToArray();
}
