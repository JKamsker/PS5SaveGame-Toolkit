using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonEvidenceTests(ITestOutputHelper output)
{
    private static readonly long[] ProfileLengths = [3993, 3522, 1312];
    private static readonly int[] ProfileCounts = [81, 70, 23];
    [Fact]
    public void ItTakesTwoGenerationsHaveStringProfilesAndF1FindsGameplayDifferences()
    {
        var files = SampleSaves.Load("PPSA02343").AllDiskFiles
            .Where(f => f.FileName == "ue4savegame.dpx.sav").ToArray();
        Assert.Equal(6, files.Length);
        var main = files.Where(f => !f.Directory.IsBackup).OrderBy(f => f.Directory.Generation).ToArray();
        Assert.Equal(ProfileLengths, main.Select(f => f.Length));
        var decoder = new JsonSaveDecoder();
        var trees = main.Select(f => decoder.Decode(f, new DecodeLimits())).ToArray();
        Assert.Equal(ProfileCounts, trees.Select(t => t.Values.Count(v => v.Path.StartsWith("Profile/", StringComparison.Ordinal))));
        foreach (var tree in trees)
            Assert.All(tree.Values.Where(v => v.Path.StartsWith("Profile/", StringComparison.Ordinal)), v => Assert.Equal("string", v.Kind));
        foreach (var file in main)
        {
            var backup = Assert.Single(files, f => f.Directory.IsBackup && f.Directory.Generation == file.Directory.Generation);
            Assert.True(file.ReadAllBytes().AsSpan().SequenceEqual(backup.ReadAllBytes()));
        }

        for (var i = 1; i < trees.Length; i++)
        {
            // Never report private profile identifiers or arbitrary values in evidence logs.
            var before = GameplayOnly(trees[i - 1]);
            var after = GameplayOnly(trees[i]);
            var diff = SemanticDiffer.Compare(before, after);
            Assert.Null(diff.Error);
            Assert.False(diff.InputTruncated);
            Assert.True(diff.Total > 0);
            output.WriteLine($"F1 generations {i - 1}/{i}: gameplay added={diff.Added}, removed={diff.Removed}, changed={diff.Changed}. Archive order is not chronology.");
        }
    }

    [Theory]
    [InlineData("PPSA04027", "settings.json", "Cyberpunk settings (version 136)")]
    [InlineData("PPSA02343", "ue4savegame.dpx.sav", "It Takes Two profile")]
    [InlineData("PPSA07696", "imgui_emu_option.json", "Ridge Racer emulator options")]
    public void KnownSamplesGetReadOnlyInterpretationAlongsideRawJson(string title, string name, string tabTitle)
    {
        var plugin = new GenericJsonPlugin();
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(f => f.FileName == name))
        {
            using var document = plugin.Open(file);
            var tabs = Assert.IsType<TabsNode>(document.BuildView());
            Assert.Contains(tabs.Tabs, t => t.Title == "JSON");
            var overlay = Assert.Single(tabs.Tabs, t => t.Title == tabTitle);
            Assert.DoesNotContain(overlay.Children, n => n is FieldNode or ActionNode);
            Assert.Equal(file.ReadAllBytes(), document.Serialize()[0].Content.ToArray());
        }
    }

    private static SemanticTree GameplayOnly(SemanticTree tree) => tree with
    {
        Values = tree.Values.Where(v => v.Path.StartsWith("Profile/Minigame.", StringComparison.Ordinal)
            || v.Path.StartsWith("Profile/MinigameFlag.", StringComparison.Ordinal) || v.Path.StartsWith("Profile/MinigameCounter.", StringComparison.Ordinal)
            || v.Path is "Profile/LastSaveChapter" or "Profile/LastSaveProgressPoint" or "Profile/FurthestUnlockedChapter").ToArray(),
    };
}
