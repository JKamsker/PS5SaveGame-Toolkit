using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Json;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class ParserDiagnosticTests
{
    [Fact]
    public void LimitDiagnosticRetainsSdkReasonAndInnerException()
    {
        var input = new byte[JsonTokenTree.MaxBytes + 1];

        var error = Assert.Throws<SaveFormatException>(() => JsonTokenTree.Parse(input));
        var sdkError = Assert.IsType<LosslessJsonException>(error.InnerException);

        Assert.Contains($"between 1 and {JsonTokenTree.MaxBytes}", error.Message);
        Assert.Null(sdkError.ByteOffset);
        Assert.DoesNotContain("maximum depth", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SyntaxDiagnosticRetainsBoundedLocation()
    {
        var input = Encoding.UTF8.GetBytes("{\"x\":}");

        AssertLocation(input);
    }

    [Fact]
    public void Utf8DiagnosticRetainsBoundedLocation()
    {
        var input = Encoding.UTF8.GetBytes("{\"x\":0}");
        input[^2] = 0xFF;

        AssertLocation(input);
    }

    private static void AssertLocation(byte[] input)
    {
        var error = Assert.Throws<SaveFormatException>(() => JsonTokenTree.Parse(input));
        var sdkError = Assert.IsType<LosslessJsonException>(error.InnerException);

        Assert.NotNull(sdkError.ByteOffset);
        Assert.Contains("byte offset", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(sdkError.ByteOffset!.Value.ToString(), error.Message, StringComparison.Ordinal);
        Assert.InRange(error.Message.Length, 1, 512);
    }
}
