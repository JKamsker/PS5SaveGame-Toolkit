using SaveToolkit.Testing;
using Xunit;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class DigestResearchTests(ITestOutputHelper output)
{
    [Fact]
    public void SampleDigestHypothesesAreReproducible()
    {
        var file = SampleSaves.Load("PPSA18463").AllDiskFiles.First(f => f.FileName == "AutoSave_0.lsv" && !f.Directory.IsBackup);
        var bytes = file.ReadAllBytes();
        var expected = LspkPackage.Read(bytes).HeaderDigest;
        var results = new DigestExperiments(bytes).Run();
        var matches = results.Where(pair => pair.Value == expected).Select(pair => pair.Key).ToArray();
        output.WriteLine($"Header: {expected}; candidates: {results.Count}; matches: {string.Join(", ", matches)}");
        Assert.Equal(["expanded/offset/forward/digest+1", "expanded/ignore-case/forward/digest+1"], matches);
        Assert.Equal("6D98C6D4C2FF81E7C7F43AAA881D66DA", expected);
        Assert.Equal(844, results.Count);
        output.WriteLine("Physical order: " + string.Join(", ", LspkPackage.Read(bytes).Entries.OrderBy(e => e.Offset).Select(e => e.Name)));
    }
}
