using System.Diagnostics;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using Xunit;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokSampleTests(ITestOutputHelper output)
{
    private readonly GodOfWarRagnarokPlugin _plugin = new();

    [Fact]
    public void EveryArchiveRecordIncludingBackupAndGenerationsOpensAndRoundTrips()
    {
        var set = SampleSaves.Load("PPSA08330");
        var files = set.AllDiskFiles.Where(f => !f.IsSystemFile).ToArray();
        Assert.Equal(156, files.Length);
        Assert.Equal(66, files.Where(f => f.FileName.StartsWith("GOWRSAVE", StringComparison.Ordinal))
            .Select(f => f.FileName).Distinct().Count());
        var maximum = TimeSpan.Zero;
        foreach (var file in files)
        {
            Assert.True(_plugin.Probe(file).IsMatch, file.RelativePath);
            var timer = Stopwatch.StartNew();
            using var document = _plugin.Open(file);
            timer.Stop();
            maximum = timer.Elapsed > maximum ? timer.Elapsed : maximum;
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), $"Opening {file.RelativePath} took {timer.Elapsed}.");
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
            Assert.False(document.CanEdit);
            var root = document.BuildView();
            Assert.All(RagnarokFormatTests.Flatten(root).OfType<FieldNode>(), f => Assert.True(f.Spec.ReadOnly));
            if (file.FileName != "userpreferences")
            {
                var stats = Assert.Single(RagnarokFormatTests.Flatten(root).OfType<StatsNode>());
                Assert.Contains(stats.Items, s => s.Label == "Indexed IDs" && s.Value == "798");
                var secondary = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("secondary/0")));
                Assert.Equal(10, secondary.Rows.Count);
            }
        }

        output.WriteLine($"Opened and byte-identically serialized {files.Length} records; slowest Open: {maximum.TotalMilliseconds:F2} ms.");
    }

    [Fact]
    public void SaveZeroKnownValuesAndShrinkingGenerationRemainDistinct()
    {
        var files = SampleSaves.Load("PPSA08330").AllDiskFiles
            .Where(f => !f.Directory.IsBackup && f.FileName == "GOWRSAVE0")
            .OrderBy(f => f.Directory.Generation).ToArray();
        Assert.Equal(2, files.Length);
        Assert.Equal(3_215_266, files[0].Length);
        Assert.Equal(834_621, files[1].Length);
        using var first = _plugin.Open(files[0]);
        var root = first.BuildView();
        Assert.Equal(138_041, first.GetValue("metadata/play-seconds").AsInt32());
        Assert.Equal(1797, first.GetValue("raw/1BC/4/decimal").AsInt32());
        Assert.Equal("Svartalfheim - Sverd-Sande - Jenseits von Ragnarök", first.GetValue("metadata/location").AsString());
        var place = Assert.Single(RagnarokFormatTests.Flatten(root).OfType<FieldNode>(), f => f.ValueId == "metadata/location");
        Assert.Equal(Confidence.Verified, place.Confidence);
        var wallet = Assert.IsType<TableNode>(Assert.Single(first.ExpandNode("wallet/0")));
        Assert.Equal(211, wallet.Rows.Count);
        AssertQuantity(wallet, "Hacksilver", 288_638);
        AssertQuantity(wallet, "Rawhide", 52);
        AssertQuantity(wallet, "Chaos Flame", 0);
        AssertQuantity(wallet, "XP (2018 EconomyXP ID; Ragnarök role inferred)", 84_725);
        var hacksilver = Assert.Single(wallet.Rows, r => r.Cells["name"] is TextNode { Text: "Hacksilver" });
        Assert.Equal("0x1BC88", Assert.IsType<TextNode>(hacksilver.Cells["offset"]).Text);
        var anchors = Assert.IsType<TableNode>(Assert.Single(first.ExpandNode("anchors/0")));
        Assert.Equal(3, anchors.Rows.Count(r => r.Cells["name"] is TextNode { Text: "Hacksilver" }));

        using var second = _plugin.Open(files[1]);
        _ = second.BuildView();
        Assert.Equal(568, second.GetValue("metadata/play-seconds").AsInt32());
        Assert.Empty(second.ExpandNode("wallet"));
    }

    [Fact]
    public void LazyTablesAndFinalRawBytesRemainBoundedAndReadable()
    {
        var file = SampleSaves.Load("PPSA08330").AllDiskFiles.First(f => f.FileName == "GOWRSAVE0");
        using var document = _plugin.Open(file);
        Assert.InRange(RagnarokFormatTests.Flatten(document.BuildView()).Count(), 1, 100);
        foreach (var kind in new[] { "directory", "secondary", "wallet", "anchors" })
        {
            foreach (var section in document.ExpandNode(kind).Cast<SectionNode>())
            {
                var table = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode(section.LazyId!)));
                Assert.InRange(table.Rows.Count, 1, 256);
                foreach (var field in RagnarokFormatTests.Flatten(table).OfType<FieldNode>())
                {
                    Assert.True(field.Spec.ReadOnly);
                    Assert.Equal(field.Value, document.GetValue(field.ValueId));
                }

                Assert.NotEmpty(System.Text.Json.JsonSerializer.Serialize<ViewNode>(table));
            }
        }

        var groups = document.ExpandNode("raw").Cast<SectionNode>().ToArray();
        Assert.InRange(groups.Length, 1, 512);
        var finalGroupPages = document.ExpandNode(groups[^1].LazyId!).Cast<SectionNode>().ToArray();
        Assert.InRange(finalGroupPages.Length, 1, 64);
        var last = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode(finalGroupPages[^1].LazyId!)));
        Assert.InRange(last.Rows.Count, 1, 258);
        var finalByte = Assert.IsType<FieldNode>(last.Rows[^1].Cells["value"]);
        Assert.Equal(file.ReadAllBytes()[^1], finalByte.Value.AsInt32());
        Assert.Equal(finalByte.Value, document.GetValue(finalByte.ValueId));
    }

    [Fact]
    public void EveryPrimaryHasAnIdenticalBackupTwin()
    {
        var files = SampleSaves.Load("PPSA08330").AllDiskFiles.Where(f => !f.IsSystemFile).ToArray();
        foreach (var primary in files.Where(f => !f.Directory.IsBackup))
        {
            var backup = Assert.Single(files, f => f.Directory.IsBackup && f.Directory.Generation == primary.Directory.Generation &&
                f.Directory.Name == primary.Directory.Name && f.PathInDirectory == primary.PathInDirectory);
            Assert.Equal(primary.ReadAllBytes(), backup.ReadAllBytes());
        }
    }

    [Fact]
    public void SampleStructuralCorruptionAndTruncationAreRejected()
    {
        var file = SampleSaves.Load("PPSA08330").AllDiskFiles.First(f => f.FileName == "GOWRSAVE0");
        foreach (var offset in new[] { 0x48, 0x440, 0x442, 0x44D, file.Length - 1 })
        {
            var bytes = file.ReadAllBytes();
            bytes[offset] ^= 0xFF;
            Assert.Throws<SaveFormatException>(() => _plugin.Open(MemorySaveSet.SingleFile(file.FileName, bytes, "PPSA08330")));
        }

        Assert.Throws<SaveFormatException>(() => _plugin.Open(MemorySaveSet.SingleFile(file.FileName, file.ReadAllBytes()[..^16], "PPSA08330")));
    }

    private static void AssertQuantity(TableNode table, string name, int expected)
    {
        var row = Assert.Single(table.Rows, r => r.Cells["name"] is TextNode text && text.Text == name);
        var field = Assert.IsType<FieldNode>(row.Cells["quantity"]);
        Assert.Equal(expected, field.Value.AsInt32());
        Assert.Equal(Confidence.Inferred, field.Confidence);
    }
}
