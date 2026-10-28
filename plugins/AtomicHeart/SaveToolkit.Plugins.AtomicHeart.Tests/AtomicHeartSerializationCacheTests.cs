using System.Diagnostics;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartSerializationCacheTests(ITestOutputHelper output)
{
    [Fact]
    public void CachedOutputsAreIsolatedAndFollowScalarReferenceUndoAndRedo()
    {
        var file = WriteTestData.Sample();
        var original = file.ReadAllBytes();
        using var document = WriteTestData.Plugin.Open(file, WriteTestData.References());
        var initial = WriteTestData.Output(document);
        initial[0] ^= 1;
        Assert.Equal(original, WriteTestData.Output(document));
        var row = WriteTestData.Table(document).Rows.Single(r => WriteTestData.Field(r, "asset").Value.AsString() == "DA_Item_Shved");
        var amount = WriteTestData.Field(row, "amount");
        var asset = WriteTestData.Field(row, "asset");
        Assert.True(document.ApplyEdits([new(amount.ValueId, FieldValue.Of(0))]).Succeeded);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var first = WriteTestData.Output(document);
        timer.Stop();
        output.WriteLine($"First changed export: {timer.Elapsed.TotalMilliseconds:F2} ms; {GC.GetAllocatedBytesForCurrentThread() - allocated:N0} allocated bytes.");
        allocated = GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();
        const int repetitions = 10;
        for (var i = 0; i < repetitions; i++) Assert.Equal(first, WriteTestData.Output(document));
        timer.Stop();
        output.WriteLine($"Repeated validated export: {timer.Elapsed.TotalMilliseconds / repetitions:F2} ms/export; {(GC.GetAllocatedBytesForCurrentThread() - allocated) / repetitions:N0} allocated bytes/export. Diagnostic only; no timing assertions.");
        var damaged = WriteTestData.Output(document);
        damaged[^1] ^= 1;
        Assert.Equal(first, WriteTestData.Output(document));
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
}
