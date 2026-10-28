using System.Diagnostics;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroSampleTests(ITestOutputHelper output)
{
    [Fact]
    public void EverySnapshotAndBackup_HasKnownValuesAndIdenticalNoOpOutput()
    {
        var files = AstroTestData.Samples();
        Assert.Equal(4, files.Count);
        foreach (var file in files)
        {
            var watch = Stopwatch.StartNew();
            using var document = new AstroPlayroomPlugin().Open(file);
            _ = document.BuildView();
            output.WriteLine($"Generation {file.Directory.Generation}, backup={file.Directory.IsBackup}: open + main view {watch.Elapsed.TotalMilliseconds:F1} ms");
            var fields = AstroTestData.Fields(document);
            Assert.Equal(508, fields.Count);
            var coins = Assert.Single(fields, f => f.Description == AstroTestData.CoinsPath);
            var playCount = Assert.Single(fields, f => f.Description == AstroTestData.CountPath);
            Assert.Equal(FieldValue.Of(file.Directory.Generation == 0 ? 0L : 1749L), coins.Value);
            Assert.Equal(FieldValue.Of(file.Directory.Generation == 0 ? 1L : 2L), playCount.Value);
            Assert.Equal(Confidence.Verified, coins.Confidence);
            Assert.Equal(Confidence.Verified, playCount.Confidence);
            Assert.Equal(file.Directory.Generation == 0 ? 0 : 3, GotCount("Artifact"));
            Assert.Equal(file.Directory.Generation == 0 ? 0 : 9, GotCount("Puzzle"));
            Assert.Equal(file.Directory.Generation == 0 ? 0 : 1, GotCount("Prop"));
            Assert.Contains(fields, f => f.Description == "PreInstall/LeaderBoard/1073741839/Score" && f.Spec.ReadOnly);
            Assert.DoesNotContain(fields, f => f.Description!.Contains("Armadillo", StringComparison.Ordinal));
            Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));

            int GotCount(string group) => fields.Count(f => f.Description!.StartsWith($"PreInstall/{group}/", StringComparison.Ordinal)
                && f.Label == "IsGot" && f.Value.AsBool());
        }

        foreach (var group in files.GroupBy(f => f.Directory.Generation))
        {
            var primary = Assert.Single(group, f => !f.Directory.IsBackup);
            var backup = Assert.Single(group, f => f.Directory.IsBackup);
            Assert.Equal(primary.ReadAllBytes(), backup.ReadAllBytes());
        }

        Assert.False(files[0].ReadAllBytes().AsSpan().SequenceEqual(files[2].ReadAllBytes()));
    }
}
