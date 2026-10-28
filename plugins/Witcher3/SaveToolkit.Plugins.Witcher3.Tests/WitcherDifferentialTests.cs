using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherDifferentialTests(ITestOutputHelper output)
{
    [Fact]
    public void F1AlignsAllFourIndependentSnapshotsByEntityAndItemIds()
    {
        string[] order = ["313c5b3", "317a3e7", "31c45eb", "31c94f5"];
        var files = SampleSaves.Load("PPSA04018").AllDiskFiles
            .Where(f => !f.Directory.IsBackup && f.FileName.EndsWith(".sav", StringComparison.Ordinal))
            .OrderBy(f => Array.FindIndex(order, suffix => f.FileName.Contains(suffix, StringComparison.Ordinal))).ToArray();
        Assert.Equal(4, files.Length);
        Assert.Equal(4, files.Select(f => f.Sha256).Distinct().Count());
        var decoder = new WitcherSemanticDecoder();
        var limits = new DecodeLimits();
        var snapshots = files.Select(f => decoder.Decode(f, limits)).ToArray();
        Assert.All(snapshots, EnabledWriteFixture.AssertOnlyPositionalIdentityLimit);
        foreach (var file in files)
        {
            var twin = Assert.Single(SampleSaves.Load("PPSA04018").AllDiskFiles,
                f => f.Directory.IsBackup && f.FileName == file.FileName);
            Assert.Equal(file.Sha256, twin.Sha256);
        }

        for (var i = 1; i < files.Length; i++)
        {
            var diff = SemanticDiffer.Compare(snapshots[i - 1], snapshots[i], maxChanges: 10_000);
            Assert.False(diff.Truncated);
            Assert.True(diff.InputTruncated);
            Assert.All(diff.Warnings, w => Assert.Contains("positional identity", w, StringComparison.Ordinal));
            output.WriteLine($"{files[i - 1].FileName} -> {files[i].FileName}\n{DiffJson.Serialize(diff)}");
            Assert.DoesNotContain(diff.Changes, c => c.Path.StartsWith("gwent/", StringComparison.Ordinal));
            var oldBread = FindInventory(snapshots[i - 1], "Bread");
            var newBread = FindInventory(snapshots[i], "Bread");
            Assert.Equal(oldBread, newBread); // The path contains entity IdTag + item instance identity.
            if (i == 1)
            {
                Assert.Contains(diff.Changes, c => c.Path == $"{oldBread}/quantity" && c.Before == "5" && c.After == "4");
            }

            var oldBolt = FindInventory(snapshots[i - 1], "Bodkin Bolt");
            var newBolt = FindInventory(snapshots[i], "Bodkin Bolt");
            if (i < 3)
            {
                Assert.NotEqual(oldBolt, newBolt);
                Assert.Contains(diff.Changes, c => c.Path == $"{oldBolt}/name" && c.Kind == SemanticChangeKind.Removed);
                Assert.Contains(diff.Changes, c => c.Path == $"{newBolt}/name" && c.Kind == SemanticChangeKind.Added);
            }
            else { Assert.Equal(oldBolt, newBolt); }
        }
    }

    [Fact]
    public void F1DecoderHonorsValueBudget()
    {
        var file = SampleSaves.Load("PPSA04018").AllDiskFiles.First(f => f.FileName.EndsWith(".sav", StringComparison.Ordinal));
        var tree = new WitcherSemanticDecoder().Decode(file, new DecodeLimits { MaxValues = 10 });
        Assert.True(tree.Truncated);
        Assert.Equal(10, tree.Values.Count);
    }

    private static string FindInventory(SemanticTree snapshot, string name) =>
        Assert.Single(snapshot.Values, v => v.Path.EndsWith("/name", StringComparison.Ordinal) && v.Value == name).Path[..^5];
}
