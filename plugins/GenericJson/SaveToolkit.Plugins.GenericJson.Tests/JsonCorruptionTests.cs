using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonCorruptionTests
{
    [Fact]
    public void SyntaxDiagnosticLocatesFailureWithoutEchoingInput()
    {
        var error = Reject(Encoding.UTF8.GetBytes("{\n  \"synthetic-marker\":\n"));
        Assert.Contains("Invalid JSON at line 3, byte position 0", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-marker", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("22FF22", 1)]
    [InlineData("EFBBBF22FF22", 4)]
    public void Utf8DiagnosticIdentifiesEncodingAndAbsoluteByteOffset(string hex, int offset)
    {
        var error = Reject(Convert.FromHexString(hex));
        Assert.Equal($"Invalid UTF-8 text at byte offset {offset}.", error.Message);
    }

    [Theory]
    [InlineData("\"\\uD800\"")]
    [InlineData("{\"\\uDC00\":0}")]
    public void UnicodeDiagnosticIdentifiesInvalidString(string json)
    {
        var error = Reject(Encoding.UTF8.GetBytes(json));
        Assert.Contains("Invalid JSON string or Unicode escape at byte offset", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"value\":[true,false,null,-12.5e+2,\"é😀\\uD83D\\uDE00\",{}]}")]
    [InlineData("[\"\\\\\\\"\\n\\t\",{\"\\u0061\":1,\"a\":2}]")]
    [InlineData("\uFEFF{\"value\":[1,2,3]}")]
    public void EveryTruncatedPrefixOfContainerRejects(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        for (var length = 0; length < bytes.Length; length++)
            _ = Reject(bytes[..length]);
        RoundTrip(bytes);
    }

    [Theory]
    [InlineData("{]")]
    [InlineData("[}")]
    [InlineData("{\"a\" 1}")]
    [InlineData("{\"a\":1,,\"b\":2}")]
    [InlineData("[1,]")]
    [InlineData("[] null")]
    [InlineData("null null")]
    [InlineData("//comment\n{}")]
    [InlineData("{\"a\":/*comment*/0}")]
    [InlineData("+1")]
    [InlineData("01")]
    [InlineData("-01")]
    [InlineData("1.")]
    [InlineData("1e+")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("\"\\x20\"")]
    [InlineData("\"\\uZZZZ\"")]
    [InlineData("\"\\uDC00\"")]
    [InlineData("\"\\uD800x\"")]
    [InlineData("\"\\uD800\\u0041\"")]
    [InlineData("\"raw\nnewline\"")]
    [InlineData("[\"raw\0zero\"]")]
    public void MalformedSyntaxRejectsThroughDocumentedException(string json) => Reject(Encoding.UTF8.GetBytes(json));

    [Theory]
    [InlineData("80")]
    [InlineData("C0AF")]
    [InlineData("C2")]
    [InlineData("E282")]
    [InlineData("E080AF")]
    [InlineData("EDA080")]
    [InlineData("F08080AF")]
    [InlineData("F4908080")]
    [InlineData("F888808080")]
    [InlineData("FF")]
    public void InvalidUtf8RejectsInValuesAndMemberNames(string hex)
    {
        var invalid = Convert.FromHexString(hex);
        Reject([0x22, .. invalid, 0x22]);
        Reject([0x7B, 0x22, .. invalid, 0x22, 0x3A, 0x30, 0x7D]);
        Reject([.. "{} "u8.ToArray(), .. invalid]);
    }

    [Theory]
    [InlineData("EF")]
    [InlineData("EFBB")]
    [InlineData("EFBBBF")]
    [InlineData("EFBBBFEFBBBF7B7D")]
    [InlineData("20EFBBBF7B7D")]
    [InlineData("7B7DEFBBBF")]
    [InlineData("FFFE7B007D00")]
    [InlineData("FEFF007B007D")]
    [InlineData("FFFE00007B0000007D000000")]
    [InlineData("0000FEFF0000007B0000007D")]
    public void UnsupportedOrMisplacedBomRejects(string hex) => Reject(Convert.FromHexString(hex));

    [Theory]
    [InlineData("{}")]
    [InlineData(" \r\n[1,null,true]\t")]
    [InlineData("\"é😀\"")]
    [InlineData("\"\\uFEFF\"")]
    public void OptionalLeadingUtf8BomAndValidUnicodeRoundTrip(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        RoundTrip(bytes);
        RoundTrip([0xEF, 0xBB, 0xBF, .. bytes]);
    }

    internal static SaveFormatException Reject(byte[] bytes)
    {
        var file = MemorySaveSet.SingleFile("corrupt.json", bytes);
        var plugin = new GenericJsonPlugin();
        Assert.False(plugin.Probe(file).IsMatch);
        var error = Assert.Throws<SaveFormatException>(() => plugin.Open(file));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        return error;
    }

    internal static void RoundTrip(byte[] bytes)
    {
        var file = MemorySaveSet.SingleFile("valid.json", bytes);
        var plugin = new GenericJsonPlugin();
        Assert.True(plugin.Probe(file).IsMatch);
        using var document = plugin.Open(file);
        _ = document.BuildView();
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
