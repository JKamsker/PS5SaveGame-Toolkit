using System.Diagnostics;
using System.Runtime.InteropServices;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartSerializationCacheTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedOutputsAreIsolatedAndFollowScalarReferenceUndoAndRedo(bool useSample)
    {
        var file = useSample ? WriteTestData.Sample()
            : MemorySaveSet.SingleFile("slot", InventoryFixture.Create(), "PPSA10695");
        var original = file.ReadAllBytes();
        using var document = WriteTestData.Plugin.Open(file, WriteTestData.References());
        Assert.Equal(original, AssertIsolatedOutput(document, damageTrailer: false));
        var row = WriteTestData.Table(document).Rows.Single(r => WriteTestData.Field(r, "asset").Value.AsString() == "DA_Item_Shved");
        var amount = WriteTestData.Field(row, "amount");
        var asset = WriteTestData.Field(row, "asset");
        Assert.True(document.ApplyEdits([new(amount.ValueId, FieldValue.Of(0))]).Succeeded);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var first = AssertIsolatedOutput(document, damageTrailer: true);
        Assert.Equal(FieldValue.Of(0), document.GetValue(amount.ValueId));
        Assert.Equal(asset.Value, document.GetValue(asset.ValueId));
        timer.Stop();
        output.WriteLine($"First changed export with isolation check: {timer.Elapsed.TotalMilliseconds:F2} ms; {GC.GetAllocatedBytesForCurrentThread() - allocated:N0} allocated bytes.");
        allocated = GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();
        const int repetitions = 10;
        for (var i = 0; i < repetitions; i++) Assert.Equal(first, WriteTestData.Output(document));
        timer.Stop();
        output.WriteLine($"Repeated validated export: {timer.Elapsed.TotalMilliseconds / repetitions:F2} ms/export; {(GC.GetAllocatedBytesForCurrentThread() - allocated) / repetitions:N0} allocated bytes/export. Diagnostic only; no timing assertions.");
        Assert.Equal(first, AssertIsolatedOutput(document, damageTrailer: true));
        Assert.True(document.ApplyEdits([new(asset.ValueId, FieldValue.Of("DA_Item_ShotgunKS23"))]).Succeeded);
        var second = WriteTestData.Output(document);
        Assert.NotEqual(first, second);
        using (var reopened = WriteTestData.Open(second))
        {
            Assert.Equal(FieldValue.Of(0), reopened.GetValue(amount.ValueId));
            Assert.Equal(FieldValue.Of("DA_Item_ShotgunKS23"), reopened.GetValue(asset.ValueId));
        }
        Assert.True(document.ApplyEdits([new(amount.ValueId, amount.Value), new(asset.ValueId, asset.Value)]).Succeeded);
        Assert.Equal(original, WriteTestData.Output(document));
        Assert.True(document.ApplyEdits([new(amount.ValueId, FieldValue.Of(0)), new(asset.ValueId, FieldValue.Of("DA_Item_ShotgunKS23"))]).Succeeded);
        Assert.Equal(second, WriteTestData.Output(document));
    }

    private static byte[] AssertIsolatedOutput(ISaveDocument document, bool damageTrailer)
    {
        var content = Assert.Single(document.Serialize()).Content;
        var expected = content.ToArray();
        // Mutate the actual returned memory; WriteTestData.Output intentionally copies it.
        Assert.True(MemoryMarshal.TryGetArray(content, out var segment));
        Assert.NotNull(segment.Array);
        segment.Array[segment.Offset + (damageTrailer ? segment.Count - 1 : 0)] ^= 1;
        Assert.Equal(expected, WriteTestData.Output(document));
        return expected;
    }
}
