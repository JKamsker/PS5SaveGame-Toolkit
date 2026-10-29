using System.Diagnostics;
using System.Globalization;

namespace SaveToolkit.Core.Tests.Detection;

/// <summary>Run alone in a fresh Release test process for first accepted-probe observations.</summary>
public sealed class StartupProbeMeasurements(ITestOutputHelper output)
{
    [Fact]
    public void EveryPluginAcceptsItsFirstAuthoredSyntheticSeed()
    {
        var fixtures = new DetectionFixtures();
        var rows = new List<string> { "plugin,milliseconds,allocatedBytes,inputBytes,score" };
        // Generate/import outside timing. No SampleSaves calls: these are authored conformance seeds.
        var seeds = fixtures.Synthetic().GroupBy(fixture => fixture.Owner.Info.Id)
            .Select(group => group.First()).ToArray();
        Assert.Equal(fixtures.Conformance.Count, seeds.Length);
        Assert.True(seeds.Length >= 17);
        foreach (var fixture in seeds)
        {
            var root = TestZips.TempDir();
            try
            {
                var set = DetectionFixtures.Import(fixture.File, root);
                var file = set.FindFile(fixture.File.RelativePath)!;
                var before = GC.GetAllocatedBytesForCurrentThread();
                var started = Stopwatch.GetTimestamp();
                var result = fixture.Owner.Probe(file);
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.True(result.IsMatch, fixture.Name);
                rows.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{fixture.Owner.Info.Id},{elapsed:F4},{allocated},{file.Length},{result.Score}"));
            }
            finally { Cleanup(root); }
        }
        foreach (var row in rows) output.WriteLine(row);
        if (Environment.GetEnvironmentVariable("SAVETOOLKIT_STARTUP_PROBES") is { Length: > 0 } report)
            File.WriteAllLines(report, rows);
    }

    private static void Cleanup(string root)
    {
        if (Path.GetDirectoryName(root) != Path.GetFullPath(SaveToolkit.Testing.TestScratch.ProcessFolder))
            throw new InvalidOperationException("Probe directory escaped scratch.");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
