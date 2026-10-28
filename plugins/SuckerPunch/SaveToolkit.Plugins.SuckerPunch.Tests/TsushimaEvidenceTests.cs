using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using static SaveToolkit.Plugins.SuckerPunch.Tests.SuckerPunchTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class TsushimaEvidenceTests
{
    [Theory]
    [InlineData("suckerpunchUser0", 199, 46, 30, 19, 0)]
    [InlineData("suckerpunchUser1", 85, 67, 59, 25, 6)]
    [InlineData("suckerpunchUser2", 89, 79, 59, 28, 0)]
    [InlineData("suckerpunchUser3", 156, 86, 59, 30, 8)]
    [InlineData("suckerpunchUser4", 1339, 152, 97, 47, 6)]
    [InlineData("suckerpunchUser5", 1357, 152, 98, 47, 6)]
    [InlineData("suckerpunchUser6", 1540, 156, 98, 47, 8)]
    [InlineData("suckerpunchUser7", 1855, 192, 123, 56, 8)]
    [InlineData("suckerpunchUser8", 8250, 339, 500, 197, 11)]
    [InlineData("suckerpunchUser9", 7499, 278, 493, 173, 12)]
    [InlineData("suckerpunchAuto0", 7605, 288, 500, 179, 12)]
    public void NamedCountsMatchSlotEvidence(string slot, int supplies, int bamboo, int ore, int flowers, int arrows)
    {
        using var document = Open(Slot(slot));
        Assert.Equal(supplies, document.GetValue(SuppliesId).AsInt32());
        Assert.Equal(bamboo, document.GetValue("inventory/IngredCoreBamboo/quantity").AsInt32());
        Assert.Equal(ore, document.GetValue("inventory/IngredCoreOre/quantity").AsInt32());
        Assert.Equal(flowers, document.GetValue("inventory/Flowers/quantity").AsInt32());
        Assert.Equal(arrows, document.GetValue(ArrowId).AsInt32());
        Assert.Equal(Confidence.Verified, Quantity(document, "ArrowNormal").Confidence);
        Assert.Equal(Confidence.Inferred, Quantity(document, "Supplies").Confidence);
        Assert.Equal(48, Inventory(document).Rows.Count);
        var raw = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("blocks/inventory")));
        Assert.Equal(48, raw.Rows.Count);
    }

    [Fact]
    public void PlayTimeAndDifficultyMatchEveryNonemptySubtitleAcrossSnapshots()
    {
        var plugin = new SuckerPunchPlugin();
        foreach (var file in SampleSaves.Load(TsushimaTitle).AllDiskFiles.Where(file => plugin.Probe(file).IsMatch))
        {
            using var document = Open(file);
            if (Word(file.ReadAllBytes(), 12) == 47)
            {
                Assert.Throws<KeyNotFoundException>(() => document.GetValue("progress/play-seconds"));
                continue;
            }

            var subtitle = file.Directory.ParamSfo?.SubTitle;
            if (string.IsNullOrWhiteSpace(subtitle))
            {
                continue;
            }

            var parts = subtitle.Split('|', StringSplitOptions.TrimEntries);
            var duration = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1].Split(':');
            var expectedMinutes = int.Parse(duration[0]) * 60 + int.Parse(duration[1]);
            var actualSeconds = document.GetValue("progress/play-seconds").AsDouble();
            Assert.Equal(expectedMinutes, (int)(actualSeconds / 60));
            Assert.Equal(parts[1] == "Einfach" ? "Easy" : "Medium", document.GetValue("progress/difficulty").AsString());
        }
    }

    [Fact]
    public void KnownAnchorOffsetsAreNotAppliedAsAbsoluteOffsets()
    {
        using var first = Open(Slot("suckerpunchUser0"));
        using var later = Open(Slot("suckerpunchUser9"));
        Assert.Contains("0x4BFCC", Quantity(first, "Supplies").Description);
        Assert.Contains("0x562D1", Quantity(later, "Supplies").Description);
    }
}
