using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;
using static SaveToolkit.SaveDiff.Tests.TestSaves;

namespace SaveToolkit.SaveDiff.Tests;

public sealed class SemanticDiffTests
{
    private static readonly DecodeLimits Limits = new();

    private static SemanticTree Json(string json) => new JsonSaveDecoder().Decode(Utf8(json), Limits);

    [Fact]
    public void JsonArrays_AlignRecordsByIdentity_NotByIndex()
    {
        var before = Json("""{"items":[{"id":1,"qty":5},{"id":2,"qty":1}]}""");
        var after = Json("""{"items":[{"id":3,"qty":9},{"id":2,"qty":1},{"id":1,"qty":6}]}""");

        var diff = SemanticDiffer.Compare(before, after);

        // Changed: items[id=1]/qty and the array length items[#]; added: id and qty of record 3.
        Assert.Equal((2, 2, 0), (diff.Changed, diff.Added, diff.Removed));
        var changed = diff.Changes.Single(c => c.Kind == SemanticChangeKind.Changed && c.Path == "items[id=1]/qty");
        Assert.Equal(("5", "6"), (changed.Before, changed.After));
        Assert.Contains(diff.Changes, c => c is { Kind: SemanticChangeKind.Changed, Path: "items[#]" });
        Assert.Contains(diff.Changes, c => c is { Kind: SemanticChangeKind.Added, Path: "items[id=3]/qty" });
    }

    [Fact]
    public void JsonArrays_WithoutUniqueIdentity_FallBackToIndex()
    {
        var tree = Json("""{"a":[{"id":1},{"id":1}],"b":[1,2]}""");

        Assert.Contains(tree.Values, v => v.Path == "a[1]/id");
        Assert.Contains(tree.Values, v => v.Path == "b[0]" && v.Kind == "number");
    }

    [Fact]
    public void JsonDecoder_KeepsExactNumbers_AndToleratesBomAndNulPadding()
    {
        byte[] content = [0xEF, 0xBB, 0xBF, .. Utf8("""{"big":12345678901234567890,"f":1.50,"s/x":"v"}"""), 0, 0, 0];

        var tree = new JsonSaveDecoder().Decode(content, Limits);

        Assert.Contains(tree.Values, v => v.Path == "big" && v.Value == "12345678901234567890");
        Assert.Contains(tree.Values, v => v.Path == "f" && v.Value == "1.50");
        Assert.Contains(tree.Values, v => v.Path == "s~1x");
    }

    [Fact]
    public void TypeChanges_AreReportedAsChanges()
    {
        var diff = SemanticDiffer.Compare(Json("""{"v":"1"}"""), Json("""{"v":1}"""));

        var change = Assert.Single(diff.Changes);
        Assert.Equal(("string", "number"), (change.BeforeKind, change.AfterKind));
    }

    [Fact]
    public void DuplicateIdentities_AreKept_AndWarned()
    {
        var tree = Json("""{"k":1,"k":2}""");

        Assert.Equal(["k", "k#2"], tree.Values.Select(v => v.Path));
        Assert.Contains(tree.Warnings, w => w.Contains("Duplicate identity", StringComparison.Ordinal));
    }

    [Fact]
    public void DecodeLimits_BoundTheTree()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 1000)) + "]";

        var tree = new JsonSaveDecoder().Decode(Utf8(json), new DecodeLimits { MaxValues = 10, MaxValueLength = 4 });
        var longValue = new JsonSaveDecoder().Decode(Utf8("""{"s":"abcdefgh"}"""), new DecodeLimits { MaxValueLength = 4 });

        Assert.Equal(10, tree.Values.Count);
        Assert.True(tree.Truncated);
        Assert.Equal("abcd…", Assert.Single(longValue.Values).Value);
    }

    [Fact]
    public void PathChurnRules_MaskSemanticChanges()
    {
        var before = Json("""{"lastSave":"2026-01-01","coins":1}""");
        var after = Json("""{"lastSave":"2026-01-02","coins":2}""");
        var churn = new ChurnFilter([new ChurnRule { Path = "last*", Reason = "save clock" }]).ForFile(Title, "SLOT", "x.json");

        var diff = SemanticDiffer.Compare(before, after, churn);

        Assert.Equal((1, 1), (diff.Changed, diff.Ignored));
        Assert.Equal("coins", Assert.Single(diff.Changes).Path);
    }

    [Fact]
    public void ReportedChanges_AreCapped_ButCounted()
    {
        var before = Json("[" + string.Join(",", Enumerable.Range(0, 100)) + "]");
        var after = Json("[" + string.Join(",", Enumerable.Range(1, 100)) + "]");

        var diff = SemanticDiffer.Compare(before, after, maxChanges: 5);

        Assert.Equal(100, diff.Changed);
        Assert.Equal(5, diff.Changes.Count);
        Assert.True(diff.Truncated);
    }

    [Fact]
    public void PluginViewDecoder_UsesValueIds_AndExpandsLazySections()
    {
        var decoder = new PluginViewDecoder(new InventoryPlugin());
        var file = MemorySaveSet.SingleFile("inv.dat", InventoryPlugin.Create((7, 1), (9, 3)));

        var tree = decoder.Decode(file, Limits);

        Assert.True(decoder.CanDecode(file));
        Assert.False(decoder.CanDecode(MemorySaveSet.SingleFile("x.bin", [1, 2, 3])));
        Assert.Equal(["summary/count", "inventory/7/count", "inventory/9/count"], tree.Values.Select(v => v.Path));
        Assert.Equal("Inventory › Items › Item 9", tree.Values[2].Label);
        Assert.DoesNotContain(tree.Values, v => v.Path == "param");
    }

    [Fact]
    public void SemanticMode_ThroughSaveSetDiffer_ReportsPluginFieldChanges()
    {
        using var before = Load((Primary("inv.dat"), InventoryPlugin.Create((7, 1), (9, 3))));
        using var after = Load((Primary("inv.dat"), InventoryPlugin.Create((9, 4), (7, 1), (11, 1))));

        var report = SaveSetDiffer.Compare(before.Set, after.Set, new SaveDiffOptions { Decoder = new PluginViewDecoder(new InventoryPlugin()) });

        var semantic = Assert.Single(report.Files).Semantic!;
        Assert.Equal("test.inventory", semantic.Decoder);
        Assert.Contains(semantic.Changes, c => c is { Path: "inventory/9/count", Before: "3", After: "4" });
        Assert.Contains(semantic.Changes, c => c is { Path: "inventory/11/count", Kind: SemanticChangeKind.Added });
        Assert.DoesNotContain(semantic.Changes, c => c.Path == "inventory/7/count");
    }

    [Fact]
    public void DecoderFailures_AreReportedPerFile_WithoutLosingTheByteDiff()
    {
        using var before = Load((Primary("inv.dat"), Utf8("INV1\nbroken")));
        using var after = Load((Primary("inv.dat"), Utf8("INV1\nbroke2")));

        var report = SaveSetDiffer.Compare(before.Set, after.Set, new SaveDiffOptions { Decoder = new PluginViewDecoder(new InventoryPlugin()) });

        var file = Assert.Single(report.Files);
        Assert.Equal(FileDiffStatus.Changed, file.Status);
        Assert.NotNull(file.Semantic!.Error);
        Assert.NotNull(file.Bytes);
    }

    [Fact]
    public void FirstMatchDecoder_PicksTheFirstDecoderThatAcceptsTheFile()
    {
        var auto = new FirstMatchDecoder([new ProbingPluginDecoder([new InventoryPlugin()]), new JsonSaveDecoder()]);

        var inventory = auto.Decode(MemorySaveSet.SingleFile("inv.dat", InventoryPlugin.Create((1, 1))), Limits);
        var json = auto.Decode(MemorySaveSet.SingleFile("a.json", Utf8("""{"a":1}""")), Limits);

        Assert.Equal("test.inventory", inventory.Decoder);
        Assert.Equal("json", json.Decoder);
        Assert.False(auto.CanDecode(MemorySaveSet.SingleFile("x.bin", [0, 1])));
    }
}
