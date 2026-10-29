using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

[Collection(AllocationMeasurements.Name)]
public sealed class EncodingPerformanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpeningLargeEncodedObjectAllocatesOnlyOneDecodedBuffer(bool xor)
    {
        const int payloadLength = 1024 * 1024;
        const int parserOverheadBudget = 128 * 1024;
        const int sdkLexemeOverhead = payloadLength * 2;
        var bytes = XorJsonTests.Encode("{\"payload\":\"" + new string('p', payloadLength) + "\",\"number\":-0.000E+001}", xor);
        var file = MemorySaveSet.SingleFile("large.sav", bytes);
        var plugin = new XorJsonPlugin();
        using (var warmup = plugin.Open(file)) { }

        var allocated = AllocationProbe.MeasureOpen(() => plugin.Open(file), out var opened);
        using var document = opened;

        // The SDK adapter intentionally retains one exact UTF-16 scalar lexeme alongside the
        // typed value for lossless edits. Bound that measured dual-model cost explicitly.
        allocated.AssertInRange(1, bytes.Length * 4L + parserOverheadBudget + sdkLexemeOverhead);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
