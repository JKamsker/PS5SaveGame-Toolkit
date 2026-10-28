using System.Text.Json;
using static SaveToolkit.SaveDiff.Tests.TestSaves;

namespace SaveToolkit.SaveDiff.Tests;

public sealed class CliTests
{
    private static readonly byte[] Data = Bytes(32, 1);

    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr, () => new DecoderResolver([new InventoryPlugin()]));
        return (exit, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Compare_ReturnsZeroForIdenticalSets_AndOneForChanges()
    {
        var dir = TempDir();
        var a = WriteZip(dir, "a.zip", (Primary("data.bin"), Data));
        var b = WriteZip(dir, "b.zip", (Primary("data.bin"), With(Data, 3, 9)));

        var same = Run("compare", a, a);
        var different = Run("compare", a, b);

        Assert.Equal(CliApp.ExitSame, same.Exit);
        Assert.Equal(CliApp.ExitDifferent, different.Exit);
        Assert.Contains("M PPSA99999/sdimg_SLOT/data.bin", different.Out, StringComparison.Ordinal);
        Assert.Contains("[09]", different.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_WritesMachineReadableJson()
    {
        var dir = TempDir();
        var a = WriteZip(dir, "a.zip", (Primary("inv.dat"), InventoryPlugin.Create((1, 1))));
        var b = WriteZip(dir, "b.zip", (Primary("inv.dat"), InventoryPlugin.Create((1, 2))));

        var (exit, output, _) = Run("compare", a, b, "--semantic", "test.inventory", "--format", "json");

        Assert.Equal(CliApp.ExitDifferent, exit);
        using var json = JsonDocument.Parse(output);
        var file = json.RootElement.GetProperty("files")[0];
        Assert.Equal("savetoolkit-savediff/1", json.RootElement.GetProperty("format").GetString());
        Assert.Equal("Changed", file.GetProperty("status").GetString());
        Assert.Equal("inventory/1/count", file.GetProperty("semantic").GetProperty("changes")[0].GetProperty("path").GetString());
    }

    [Fact]
    public void Compare_TwoGenerationsOfOneDump_AndIgnoreFile()
    {
        var dir = TempDir();
        var dump = WriteZip(dir, "dump.zip", (Primary("data.bin"), Data), (Primary("data.bin"), With(Data, 0, 7)));
        var ignore = Path.Combine(dir, "ignore.json");
        File.WriteAllText(ignore, """{"ignore":[{"file":"data.bin","offset":0,"length":1,"reason":"counter"}]}""");

        var raw = Run("compare", dump, dump, "--before-gen", "0", "--after-gen", "1");
        var masked = Run("compare", dump, dump, "--before-gen", "0", "--after-gen", "latest", "--ignore", ignore);

        Assert.Equal(CliApp.ExitDifferent, raw.Exit);
        Assert.Equal(CliApp.ExitSame, masked.Exit);
        Assert.Contains("ChurnOnly", masked.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void List_ShowsGenerationsAndBackups()
    {
        var dump = WriteZip(TempDir(), "dump.zip", (Primary("data.bin"), Data), (Backup("data.bin"), Data), (Primary("data.bin"), Data));

        var (exit, output, _) = Run("list", dump);

        Assert.Equal(CliApp.ExitSame, exit);
        Assert.Contains("2 generation(s)", output, StringComparison.Ordinal);
        Assert.Contains("g1 PPSA99999/sdimg_SLOT", output, StringComparison.Ordinal);
        Assert.Contains("sdimg_sce_bu_SLOT (backup)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_PassesFailsAndSkipsFixtures()
    {
        var dir = TempDir();
        WriteZip(dir, "before.zip", (Primary("data.bin"), Data));
        WriteZip(dir, "after.zip", (Primary("data.bin"), With(Data, 4, 2)));
        var manifest = Path.Combine(dir, "fixtures.json");
        File.WriteAllText(manifest, """
            {"fixtures":[
              {"id":"ok","titleId":"PPSA99999","event":"e","before":{"archive":"before.zip"},"after":{"archive":"after.zip"},
               "expected":[{"file":"data.bin","offset":4,"length":1,"before":"01","after":"02"}],"strict":true},
              {"id":"bad","titleId":"PPSA99999","event":"e","before":{"archive":"before.zip"},"after":{"archive":"after.zip"},
               "expected":[{"file":"data.bin","offset":5,"length":1}]},
              {"id":"absent","titleId":"PPSA99999","event":"e","before":{"archive":"nope.zip"},"after":{"archive":"after.zip"}}
            ]}
            """);

        var all = Run("verify", manifest, "--artifacts", dir);
        var onlyGood = Run("verify", manifest, "--artifacts", dir, "--fixture", "ok", "--fixture", "absent");

        Assert.Equal(CliApp.ExitDifferent, all.Exit);
        Assert.Contains("1 passed, 1 failed, 1 skipped", all.Out, StringComparison.Ordinal);
        Assert.Equal(CliApp.ExitSame, onlyGood.Exit);
    }

    [Theory]
    [InlineData("compare", "only-one.zip")]
    [InlineData("compare", "a", "b", "--bogus")]
    [InlineData("compare", "a", "b", "--before-gen", "first")]
    [InlineData("frobnicate")]
    public void UsageErrors_ReturnTwo(params string[] args)
    {
        var (exit, _, err) = Run(args);

        Assert.Equal(CliApp.ExitError, exit);
        Assert.StartsWith("savediff:", err, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingInputAndUnknownDecoder_ReturnTwo()
    {
        var a = WriteZip(TempDir(), "a.zip", (Primary("data.bin"), Data));

        Assert.Equal(CliApp.ExitError, Run("compare", a, Path.Combine(TempDir(), "missing.zip")).Exit);
        Assert.Contains("Unknown decoder", Run("compare", a, a, "--semantic", "nope").Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Out_IsRefusedInsideGitWorkingTreesAndArtifacts_ButAllowedElsewhere()
    {
        var dir = TempDir();
        var a = WriteZip(dir, "a.zip", (Primary("data.bin"), Data));
        var repo = Path.Combine(dir, "repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var artifacts = Path.Combine(dir, "artifacts");
        Directory.CreateDirectory(artifacts);

        var inRepo = Run("compare", a, a, "--out", Path.Combine(repo, "sub", "report.txt"));
        var inArtifacts = Run("compare", a, a, "--out", Path.Combine(artifacts, "report.txt"));
        var outside = Run("compare", a, a, "--out", Path.Combine(dir, "report.txt"));

        Assert.Equal(CliApp.ExitError, inRepo.Exit);
        Assert.Equal(CliApp.ExitError, inArtifacts.Exit);
        Assert.Equal(CliApp.ExitSame, outside.Exit);
        Assert.True(File.Exists(Path.Combine(dir, "report.txt")));
        Assert.False(Directory.Exists(Path.Combine(repo, "sub")));
    }
}
