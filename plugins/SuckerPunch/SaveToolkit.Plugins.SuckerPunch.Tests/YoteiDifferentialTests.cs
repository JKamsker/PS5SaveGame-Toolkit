using SaveToolkit.Sdk.Diff;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;
using static SaveToolkit.Plugins.SuckerPunch.Tests.YoteiTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class YoteiDifferentialTests
{
    private static HookSaveDecoder Native => new(Assert.Single(((ISaveDecoderProvider)new SuckerPunchPlugin()).Decoders));

    [Fact]
    public void F1ComparesEveryNonemptySlotPairAndConfirmsStationaryRegistries()
    {
        var files = Files.Where(file => Word(file.ReadAllBytes(), 0) != 0).ToArray();
        Assert.True(files.Length >= 2);
        var pairs = 0;
        var monChanges = 0;
        for (var before = 0; before < files.Length; before++)
            for (var after = before + 1; after < files.Length; after++)
            {
                // F1 pairs matching directory identities, so normalize only the in-memory wrapper.
                // The original slot names are report labels; no metadata or sample bytes are changed.
                var a = new MemorySaveSet().With(Title, "comparison", new Dictionary<string, byte[]> { ["dis.dat"] = files[before].ReadAllBytes() });
                var b = new MemorySaveSet().With(Title, "comparison", new Dictionary<string, byte[]> { ["dis.dat"] = files[after].ReadAllBytes() });
                var report = SaveSetDiffer.Compare(a, b, new SaveDiffOptions { Decoder = Native }, files[before].Directory.Name, files[after].Directory.Name);
                var diff = Assert.Single(report.Files);
                Assert.Equal(FileDiffStatus.Changed, diff.Status);
                Assert.True(diff.Bytes!.ChangedBytes > 0);
                Assert.NotNull(diff.Semantic);
                Assert.Null(diff.Semantic.Error);
                Assert.False(diff.Semantic.InputTruncated);
                Assert.False(diff.Semantic.Truncated);
                Assert.Equal(0, diff.Semantic.Added);
                Assert.Equal(0, diff.Semantic.Removed);
                Assert.DoesNotContain(diff.Semantic.Changes, change => change.Path.StartsWith("yotei/gear/", StringComparison.Ordinal));
                Assert.DoesNotContain(diff.Semantic.Changes, change => change.Path.StartsWith("yotei/registry/", StringComparison.Ordinal));
                if (diff.Semantic.Changes.Any(change => change.Path == MonId)) monChanges++;
                pairs++;
            }
        Assert.Equal(files.Length * (files.Length - 1) / 2, pairs);
        Assert.True(monChanges > 0);
    }

    [Fact]
    public void NativeSemanticEditChangesOnlyQuantityCopiesAndChecksumPaths()
    {
        var file = Slot("suckerpunchUser4");
        using var doc = Open(file);
        var before = Native.Decode(file, new DecodeLimits());
        Assert.True(doc.ApplyEdits([new(MonId, SaveToolkit.Sdk.Values.FieldValue.Of(100L)),
            new(ArrowId, SaveToolkit.Sdk.Values.FieldValue.Of(5L))]).Succeeded);
        var after = Native.Decode(WithBytes(Assert.Single(doc.Serialize()).Content.ToArray()), new DecodeLimits());
        var diff = SemanticDiffer.Compare(before, after);
        Assert.Equal<string>(["container/word3", ArrowId, "yotei/inventory/ArrowNormal/quantity-copy", MonId],
            diff.Changes.Select(change => change.Path));
    }

    [Fact]
    public void NativeAndViewDecodersHonorDepthValueAndQueryBudgets()
    {
        var file = Slot("suckerpunchUser4");
        Assert.True(Native.Decode(file, new DecodeLimits { MaxDepth = 3 }).Truncated);
        Assert.False(Native.Decode(file, new DecodeLimits { MaxDepth = 4 }).Truncated);
        var capped = Native.Decode(file, new DecodeLimits { MaxValues = 8 });
        Assert.True(capped.Truncated);
        Assert.Equal(8, capped.Values.Count);
        var view = new PluginViewDecoder(new SuckerPunchPlugin());
        var complete = view.Decode(file, new DecodeLimits { QueryPageSize = 61 });
        Assert.False(complete.Truncated);
        Assert.Equal(2074, complete.Values.Count(value => value.Path.StartsWith("yotei/gear/", StringComparison.Ordinal)));
        Assert.True(view.Decode(file, new DecodeLimits { MaxQueryRows = 1, QueryPageSize = 1 }).Truncated);
    }
}
