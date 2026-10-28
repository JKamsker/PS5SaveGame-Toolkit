using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

[Collection(nameof(GvasProbeSampleCollection))]
public sealed class GvasProbeSampleTests
{
    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void GearboxVariantSamples_WithGvasMagic_AreNotClaimed(string title)
    {
        var plugin = new UnrealGvasPlugin();
        var files = SampleSaves.Load(title).AllDiskFiles
            .Where(file => file.ReadHeader(4).AsSpan().SequenceEqual("GVAS"u8)).ToArray();
        Assert.Equal(12, files.Length);
        Assert.Contains(files, file => file.FileName == "1");
        foreach (var file in files)
        {
            Assert.Throws<SaveFormatException>(() => plugin.Open(file));
            Assert.False(plugin.Probe(file).IsMatch, file.RelativePath);
        }
    }

    [Fact]
    public void AcrossAllSampleArchives_ProbeClaimsExactlyTheFilesThatOpen()
    {
        if (SampleSaves.ArtifactsDirectory is null) Assert.Skip("Sample artifacts not found.");
        // Keep the known 60-file coverage assertion meaningful when only some archives are installed.
        foreach (var title in new[] { "PPSA01288", "PPSA01603", "PPSA02199" }) _ = SampleSaves.Load(title);
        var titles = Directory.EnumerateFiles(SampleSaves.ArtifactsDirectory, "PPSA*_saves_dec*.zip")
            .Select(path => Path.GetFileName(path).Split('_')[0]).Distinct(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(titles);
        var plugin = new UnrealGvasPlugin();
        var claimed = 0;
        foreach (var title in titles)
        foreach (var file in SampleSaves.Load(title).AllDiskFiles)
        {
            var probe = plugin.Probe(file);
            try
            {
                using var document = plugin.Open(file);
                Assert.True(probe.IsMatch, $"Openable sample was not claimed: {file.RelativePath}");
                claimed++;
            }
            catch (SaveFormatException)
            {
                Assert.False(probe.IsMatch, $"Claimed sample could not open: {file.RelativePath}");
            }
        }
        Assert.Equal(60, claimed);
    }
}

// Run corpus imports and timed sample opens serially, apart from other codec-heavy tests.
[CollectionDefinition(nameof(GvasProbeSampleCollection), DisableParallelization = true)]
public sealed class GvasProbeSampleCollection;
