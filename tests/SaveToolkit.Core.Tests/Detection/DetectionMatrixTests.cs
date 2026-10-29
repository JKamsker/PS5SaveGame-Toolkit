using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests.Detection;

public sealed class DetectionMatrixTests(ITestOutputHelper output)
{
    [Fact]
    public void SyntheticSeedsRouteIndependentlyOfRegistrationOrder()
    {
        var fixtures = new DetectionFixtures();
        var check = new DetectionMatrixAssertions(fixtures.Catalog);
        var rows = new List<string>();
        var count = 0;
        var mutations = 0;
        foreach (var fixture in fixtures.Synthetic())
        {
            var root = TestZips.TempDir();
            try
            {
                var set = DetectionFixtures.Import(fixture.File, root);
                var file = Assert.IsType<DiskSaveFile>(set.FindFile(fixture.File.RelativePath));
                var imported = fixture with { File = file };
                var matches = check.Owned(imported);
                rows.Add($"synthetic {fixture.Name} => {DetectionMatrixAssertions.Names(matches)}");
                using var workspace = new Workspace("matrix", "matrix", set, fixtures.Catalog);
                Assert.Equal(matches.Select(match => match.Plugin.Info.Id),
                    workspace.Probe(file).Select(match => match.Plugin.Info.Id));
                Assert.Equal(matches[0].Plugin.Info.Id, workspace.Open(file.Id, pluginId: null).Plugin.Info.Id);
                mutations += check.Mutated(imported);
                count++;
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        output.WriteLine($"{fixtures.Conformance.Count} plugins; {count} synthetic seeds; {mutations} mutations; 5 registration permutations.");
        AssertSnapshot("synthetic ", rows);
        AssertSnapshot("plugin ", fixtures.Catalog.Plugins.Select(plugin => $"plugin {plugin.Info.Id}"));
    }

    [Fact]
    public void AllAvailableSampleFilesHaveNoForeignClaims()
    {
        var fixtures = new DetectionFixtures();
        var check = new DetectionMatrixAssertions(fixtures.Catalog);
        var rows = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        var ownedCount = 0;
        var mutations = 0;
        var archives = 0;
        foreach (var fixture in fixtures.Conformance)
        {
            foreach (var title in fixture.Titles)
            {
                var paths = SampleSaves.ArtifactsDirectory is { } directory
                    ? Directory.EnumerateFiles(directory, $"{title}_saves_dec*.zip").Order(StringComparer.Ordinal).ToArray() : [];
                foreach (var archive in paths)
                {
                    archives++;
                    var root = TestZips.TempDir();
                    try
                    {
                        var set = SaveSetImporter.Import([new ImportSource(Path.GetFileName(archive), () => File.OpenRead(archive))], root);
                        var claimed = 0;
                        foreach (var file in set.AllDiskFiles.Where(file => !file.IsSystemFile))
                        {
                            var name = $"{fixture.Plugin.Info.Id}/{title}/{file.FileName}";
                            var matches = check.Check(file, name);
                            if (!DetectionMatrixAssertions.IsGeneric(fixture.Plugin))
                                Assert.All(matches.Where(match => match.Plugin is not RawFilePlugin
                                    && !DetectionMatrixAssertions.IsGeneric(match.Plugin)), match =>
                                    Assert.True(match.Plugin.Info.Id == fixture.Plugin.Info.Id,
                                        $"{name}: foreign claimant {match.Plugin.Info.Id}."));
                            rows.Add($"sample {name} => {DetectionMatrixAssertions.Names(matches)}");
                            Assert.NotEmpty(matches); // The production raw viewer must remain available.
                            var claimedByOwner = matches.Any(match => match.Plugin.Info.Id == fixture.Plugin.Info.Id);
                            var owner = claimedByOwner ? fixture.Plugin : matches[0].Plugin;
                            var owned = new DetectionFixture(owner, file, name);
                            check.Owned(owned);
                            mutations += check.Mutated(owned);
                            count++;
                            if (!claimedByOwner) continue;
                            claimed++;
                            ownedCount++;
                        }
                        Assert.True(claimed > 0, $"{fixture.Plugin.Info.Id}/{title}: no owned sample files.");
                    }
                    finally
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            }
        }
        Assert.SkipWhen(archives == 0, "No external detection samples; synthetic matrix always runs.");
        output.WriteLine($"{archives} plugin/archive pairs; {count} sample files ({ownedCount} owner claims); {mutations} mutations; 5 registration permutations.");
        foreach (var row in rows.Order(StringComparer.Ordinal)) output.WriteLine(row);
        // Available subsets must remain useful: compare only owners present in this run.
        var expected = SnapshotRows("sample ").Where(row => rows.Any(actual => SampleTitle(actual) == SampleTitle(row)));
        Assert.Equal(expected.Order(StringComparer.Ordinal), rows.Order(StringComparer.Ordinal));
    }

    private static string SampleTitle(string row) => string.Join('/', row.Split('/').Take(2));

    private void AssertSnapshot(string prefix, IEnumerable<string> rows)
    {
        var actual = rows.Order(StringComparer.Ordinal).ToArray();
        foreach (var row in actual) output.WriteLine(row);
        Assert.Equal(SnapshotRows(prefix).Order(StringComparer.Ordinal), actual);
    }

    private static IEnumerable<string> SnapshotRows(string prefix)
    {
        using var stream = typeof(DetectionMatrixTests).Assembly.GetManifestResourceStream(
            "SaveToolkit.Core.Tests.Detection.DetectionMatrix.txt")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(row => row.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
    }
}
