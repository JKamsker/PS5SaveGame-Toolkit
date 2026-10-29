using System.Text.Json;
using static SaveToolkit.SaveDiff.Tests.TestSaves;

namespace SaveToolkit.SaveDiff.Tests;

public sealed class CliPathTests
{
    [Fact]
    public void Compare_CaseDistinctPaths_AreOnlyReusedWhenTheyIdentifyTheSameFile()
    {
        var dir = TempDir();
        var upper = WriteZip(dir, "A.zip", (Primary("data.bin"), [1]));
        var lower = WriteZip(dir, "a.zip", (Primary("data.bin"), [2]));
        var distinct = !File.ReadAllBytes(upper).SequenceEqual(File.ReadAllBytes(lower));

        var result = Run("compare", upper, lower, "--format", "json");

        Assert.Equal(distinct ? CliApp.ExitDifferent : CliApp.ExitSame, result.Exit);
        Assert.Empty(result.Err);
        using var json = JsonDocument.Parse(result.Out);
        Assert.Equal(distinct ? "Changed" : "Unchanged", json.RootElement.GetProperty("files")[0].GetProperty("status").GetString());
    }

    [Fact]
    public void Verify_Cache_DoesNotConflateCaseDistinctArchives()
    {
        var dir = TempDir();
        var upper = WriteZip(dir, "A.zip", (Primary("data.bin"), [1]));
        var lower = WriteZip(dir, "a.zip", (Primary("data.bin"), [2]));
        var distinct = !File.ReadAllBytes(upper).SequenceEqual(File.ReadAllBytes(lower));
        var manifest = Path.Combine(dir, "fixtures.json");
        File.WriteAllText(manifest, $$"""
            {"fixtures":[{"id":"case-paths","titleId":"PPSA99999","event":"synthetic",
              "before":{"archive":"A.zip"},"after":{"archive":"a.zip"},
              "expected":[{"file":"data.bin","offset":0,"length":1,"before":"{{(distinct ? "01" : "02")}}","after":"02","changed":{{(distinct ? "true" : "false")}}}]}]}
            """);

        var result = Run("verify", manifest, "--artifacts", dir);

        Assert.Equal(CliApp.ExitSame, result.Exit);
        Assert.Empty(result.Err);
        Assert.Contains("1 passed, 0 failed, 0 skipped", result.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("compare")]
    [InlineData("verify")]
    public void MissingAndInvalidPaths_ReturnTwoWithoutAReport(string command)
    {
        var missing = Path.Combine(TempDir(), "missing.zip");
        var paths = new[] { missing, "bad\0path" };
        foreach (var path in paths)
        {
            var result = command == "compare" ? Run(command, path, path) : Run(command, path);

            Assert.Equal(CliApp.ExitError, result.Exit);
            Assert.Empty(result.Out);
            Assert.StartsWith("savediff:", result.Err, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void InvalidArchivesAndUnwritableReportPaths_ReturnTwoAndPreserveInputs()
    {
        var dir = TempDir();
        var invalid = Path.Combine(dir, "invalid.zip");
        File.WriteAllText(invalid, "not a zip");
        var save = WriteZip(dir, "valid.zip", (Primary("data.bin"), [1]));
        var original = File.ReadAllBytes(save);

        Assert.Equal(CliApp.ExitError, Run("list", invalid).Exit);
        Assert.Equal(CliApp.ExitError, Run("compare", save, invalid).Exit);
        Assert.Equal(CliApp.ExitError, Run("compare", save, save, "--out", Path.Combine(dir, "missing", "report.txt")).Exit);
        Assert.Equal(CliApp.ExitError, Run("compare", save, save, "--out", dir).Exit);
        Assert.Equal(original, File.ReadAllBytes(save));
    }

    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr, () => new DecoderResolver([]));
        return (exit, stdout.ToString(), stderr.ToString());
    }
}
