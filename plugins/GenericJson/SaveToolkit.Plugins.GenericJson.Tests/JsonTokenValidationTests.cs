using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonTokenValidationTests
{
    [Theory]
    [InlineData("{\"a\":-0,\"a\":1e999999999999999999999,\"s\":\"\\uD83D\\uDE00\"}")]
    [InlineData("\uFEFF [null,true,false,\"é\",[1.2300],{}] \r\n")]
    [InlineData("{\"\\u0061\":[],\"a\":{\"x\":1}}")]
    public void StreamingValidationAcceptsExactLosslessModel(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        var expected = JsonTokenTree.Parse(bytes);
        JsonTokenValidation.Validate(bytes);
        JsonTokenValidation.Validate(bytes, expected);
        Assert.Equal(bytes, JsonTreeWriter.Write(expected));
    }

    [Theory]
    [InlineData("[1]", "[1,2]")]
    [InlineData("[1,2]", "[1]")]
    [InlineData("[[1],2]", "[[1,2]]")]
    [InlineData("[[],[]]", "[[[]]]")]
    [InlineData("{\"a\":1,\"a\":2}", "{\"a\":2,\"a\":1}")]
    [InlineData("{\"a\":1}", "{\"b\":1}")]
    [InlineData("1.00", "1.0")]
    [InlineData("-0", "0")]
    [InlineData("1", "\"1\"")]
    [InlineData("true", "false")]
    [InlineData("null", "\"\"")]
    [InlineData("{}", "[]")]
    [InlineData("{\"a\":1}", "{}")]
    [InlineData("[]", "[[]]")]
    [InlineData("1e999999999999999999999", "1e999999999999999999998")]
    public void StreamingComparisonRejectsEveryDifferentTokenOrHierarchy(string model, string output)
    {
        var expected = JsonTokenTree.Parse(model);
        Assert.Throws<SaveFormatException>(() => JsonTokenValidation.Validate(Encoding.UTF8.GetBytes(output), expected));
    }

    [Theory]
    [InlineData("\"\\uD800\"")]
    [InlineData("{\"\\uDC00\":1}")]
    [InlineData("[1,]")]
    [InlineData("{}{}")]
    [InlineData(" ")]
    public void StreamingProbeRejectsMalformedUnicodeAndSyntax(string source) =>
        Assert.Throws<SaveFormatException>(() => JsonTokenValidation.Validate(Encoding.UTF8.GetBytes(source)));

    [Fact]
    public void LongEscapedStringsUseTheSameUnicodeChecks()
    {
        var source = "\"" + new string('x', 100_000) + "\\uD83D\\uDE00\"";
        JsonTokenValidation.Validate(Encoding.UTF8.GetBytes(source));
        Assert.Throws<SaveFormatException>(() => JsonTokenValidation.Validate(Encoding.UTF8.GetBytes(source[..^7] + "\"")));
        Assert.Throws<SaveFormatException>(() => JsonTokenValidation.Validate([(byte)'"', 0xFF, (byte)'"']));
    }
}
