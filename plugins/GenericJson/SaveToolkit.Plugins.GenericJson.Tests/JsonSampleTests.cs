using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonSampleTests
{
    [Theory]
    [InlineData("PPSA04027")]
    [InlineData("PPSA07696")]
    [InlineData("PPSA02343")]
    public void EveryJsonSnapshotAndBackupRoundTrips(string title)
    {
        var plugin = new GenericJsonPlugin();
        var files = SampleSaves.Load(title).AllDiskFiles.Where(f => plugin.Probe(f).IsMatch).ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            Assert.Equal(ProbeScore.Format, plugin.Probe(file).Score);
            using var document = plugin.Open(file);
            Assert.Equal(file.ReadAllBytes(), document.Serialize()[0].Content.ToArray());
        }
    }

    [Fact]
    public void CyberpunkSettingsHaveKnownVersionAndLength()
    {
        var file = SampleSaves.Load("PPSA04027").AllDiskFiles.First(f => f.FileName == "settings.json");
        Assert.Equal(45415, file.Length);
        using var document = new GenericJsonPlugin().Open(file);
        var version = JsonEditingTests.Fields(document).First(f => f.Label == "version");
        Assert.Equal(136, version.Value.AsInt32());
    }

    [Fact]
    public void RidgeRacerEmulatorHasKnownCubicOption()
    {
        var file = SampleSaves.Load("PPSA07696").AllDiskFiles.First(f => f.FileName == "imgui_emu_option.json");
        Assert.Equal(1320, file.Length);
        using var document = new GenericJsonPlugin().Open(file);
        var cubic = JsonEditingTests.Fields(document).Single(f => f.Label == "cubic_A");
        Assert.Equal(-0.5, cubic.Value.AsDouble());
    }
}
