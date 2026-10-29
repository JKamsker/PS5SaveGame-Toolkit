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
        var bytes = XorJsonTests.Encode("{\"payload\":\"" + new string('p', payloadLength) + "\",\"number\":-0.000E+001}", xor);
        var file = MemorySaveSet.SingleFile("large.sav", bytes);
        var plugin = new XorJsonPlugin();
        using (var warmup = plugin.Open(file)) { }

        var before = GC.GetAllocatedBytesForCurrentThread();
        using var document = plugin.Open(file);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Owned input + decoded UTF-8 + UTF-16 string; no second impossible transform.
        Assert.InRange(allocated, 1, bytes.Length * 4L + parserOverheadBudget);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
