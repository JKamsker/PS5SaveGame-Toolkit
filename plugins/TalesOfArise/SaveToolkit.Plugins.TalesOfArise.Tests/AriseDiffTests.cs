using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseDiffTests
{
    [Fact]
    public void SectionDiffAlignsMovingPhysicalSectionsBySchemaId()
    {
        var files = AriseSampleTests.GameplayFiles().Where(f => f.FileName == "SaveData_000.sav").ToArray();
        var primary = AriseFormat.Decode(files[0].ReadAllBytes());
        var twin = AriseFormat.Decode(files[1].ReadAllBytes());
        var diffs = AriseDiff.Compare(primary, twin);
        Assert.Equal(30, diffs.Count);
        Assert.All(diffs, diff => Assert.Equal(0, diff.ChangedBytes));
    }

    [Fact]
    public void SectionDiffReportsRelativeOffsetsForDifferentSlots()
    {
        var left = AriseFormat.Decode(AriseSampleTests.GameplayFiles().First(f => f.FileName == "SaveData_000.sav").ReadAllBytes());
        var right = AriseFormat.Decode(AriseSampleTests.GameplayFiles().First(f => f.FileName == "SaveData_001.sav").ReadAllBytes());
        var party = Assert.Single(AriseDiff.Compare(left, right), diff => diff.Name == "PartyProfile");
        Assert.Contains(0xAF8, party.ChangedOffsets);
        Assert.DoesNotContain(left.Sections.Single(s => s.Name == "PartyProfile").Offset, party.ChangedOffsets);
    }

    [Fact]
    public void SharedPluginDecoderExposesStableFieldPathsForSaveDiff()
    {
        var file = AriseSampleTests.GameplayFiles().First();
        var decoder = new PluginViewDecoder(new TalesOfArisePlugin());
        Assert.True(decoder.CanDecode(file));
        // F12 also visits the rich F8 evidence cells alongside the full primitive explorer.
        var tree = decoder.Decode(file, new DecodeLimits
        {
            MaxValues = 1_000_000,
            MaxQueryRows = 200_000,
            MaxQueryBytes = 256L * 1024 * 1024,
        });
        Assert.False(tree.Truncated, string.Join(';', tree.Warnings));
        Assert.Contains(tree.Values, value => value.Path.Contains("party/gald", StringComparison.Ordinal));
        Assert.Contains(tree.Values, value => value.Path.Contains("inventory/", StringComparison.Ordinal));
    }
}
