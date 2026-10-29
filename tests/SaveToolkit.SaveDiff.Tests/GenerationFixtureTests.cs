using System.Text.Json;
using SaveToolkit.Testing;

namespace SaveToolkit.SaveDiff.Tests;

public sealed class GenerationFixtureTests
{
    [Fact]
    public void CommittedFfxviGenerationsFixture_PassesWithCurrentDiscoveredDecoder()
    {
        // Use the real committed manifest, not a second copy of its expected paths/values in C#.
        if (SampleSaves.ArtifactsDirectory is not { } artifacts || !File.Exists(Path.Combine(artifacts, "PPSA10665_saves_dec.zip")))
            Assert.Skip("The exact private archive recorded by the FFXVI generations fixture is unavailable.");

        var manifest = Path.Combine(AppContext.BaseDirectory, "Fixtures", "generations.fixture.json");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = CliApp.Run(["verify", manifest, "--artifacts", SampleSaves.ArtifactsDirectory!, "--format", "json"], stdout, stderr);

        Assert.Equal(CliApp.ExitSame, exit);
        Assert.Empty(stderr.ToString());
        using var json = JsonDocument.Parse(stdout.ToString());
        var fixture = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal("Pass", fixture.GetProperty("outcome").GetString());
        var verification = fixture.GetProperty("verification");
        Assert.True(verification.GetProperty("passed").GetBoolean());
        var expectations = verification.GetProperty("expectations");
        Assert.Equal(4, expectations.GetArrayLength());
        Assert.All(expectations.EnumerateArray(), e => Assert.True(e.GetProperty("passed").GetBoolean()));
        var semantic = Assert.Single(verification.GetProperty("report").GetProperty("files").EnumerateArray()).GetProperty("semantic");
        Assert.Equal("ffxvi-evidence", semantic.GetProperty("decoder").GetString());
        Assert.False(semantic.GetProperty("inputTruncated").GetBoolean());
        Assert.False(semantic.TryGetProperty("error", out _));
    }
}
