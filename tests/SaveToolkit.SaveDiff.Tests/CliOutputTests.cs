using System.Globalization;
using System.Text;
using System.Text.Json;
using static SaveToolkit.SaveDiff.Tests.TestSaves;

namespace SaveToolkit.SaveDiff.Tests;

public sealed class CliOutputTests
{
    [Fact]
    public void Compare_Text_HasExactStableFormattingAndLfLineEndings()
    {
        var dir = TempDir();
        var before = WriteZip(dir, "before.zip", (Primary("nested/data.bin"), [1]));
        var after = WriteZip(dir, "after.zip", (Primary("nested/data.bin"), [2]));
        var expected = $"before: {Display(before)} [generation all] 1 files in 1 dirs, 1 generation(s), titles PPSA99999; 0 backup twins identical\n"
            + $"after:  {Display(after)} [generation all] 1 files in 1 dirs, 1 generation(s), titles PPSA99999; 0 backup twins identical\n"
            + "files:  1 changed, 0 added, 0 removed, 0 churn-only, 0 mirror main, 0 unchanged\n"
            + "bytes:  1 changed, 0 masked as churn; semantic: 0 changes, 0 masked, 0 decode failures\n"
            + "\nM PPSA99999/sdimg_SLOT/nested/data.bin [g0→g0] 1 → 1 bytes  Changed\n"
            + "    1 byte(s) changed in 1 range(s), 0 masked\n"
            + "    0x00000000 +1 Modified\n"
            + "      before [01]\n"
            + "      after  [02]\n";

        Assert.Equal(expected, Run("compare", before, after).Out);
        Assert.Equal(expected, Run("compare", before, after).Out);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("json")]
    public void Reports_AreIdenticalAcrossRunsCulturesAndStdoutOrUtf8File(string format)
    {
        var dir = TempDir();
        var before = WriteZip(dir, "before.zip", (Primary("z.json"), Utf8("{\"gold\":1.25}")), (Primary("A.bin"), [1]));
        var after = WriteZip(dir, "after.zip", (Primary("A.bin"), [2]), (Primary("z.json"), Utf8("{\"gold\":2.5}")));
        string[] args = ["compare", before, after, "--decoder", "json", "--format", format];
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var first = Run(args);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var second = Run(args);
            var reportPath = Path.Combine(dir, "report." + format);
            var written = Run([.. args, "--out", reportPath]);

            Assert.Equal(CliApp.ExitDifferent, first.Exit);
            Assert.Equal(first, second);
            Assert.Equal(first.Exit, written.Exit);
            Assert.Empty(first.Err);
            Assert.Empty(written.Err);
            Assert.Equal($"Report written to {Display(reportPath)}\n", written.Out);
            Assert.DoesNotContain('\r', first.Out);
            Assert.EndsWith("\n", first.Out, StringComparison.Ordinal);
            Assert.Equal(Encoding.UTF8.GetBytes(first.Out), File.ReadAllBytes(reportPath));
            if (format == "json")
            {
                using var json = JsonDocument.Parse(first.Out);
                Assert.Equal(Display(before), json.RootElement.GetProperty("before").GetProperty("label").GetString());
                Assert.Equal(["A.bin", "z.json"], json.RootElement.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("path").GetString()));
                Assert.Equal("2.5", json.RootElement.GetProperty("files")[1].GetProperty("semantic").GetProperty("changes")[0].GetProperty("after").GetString());
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("text")]
    [InlineData("json")]
    public void List_UsesForwardSlashSavePathsAndStableDirectoryFileOrder(string format)
    {
        var zip = WriteZip(TempDir(), "save.zip", (Primary("z.bin"), [1]), (Backup("a.bin"), [2]),
            (Primary("nested/b.bin"), [3]), (Primary("a.bin"), [4]));

        var first = Run("list", zip, "--format", format);

        Assert.Equal(CliApp.ExitSame, first.Exit);
        Assert.Empty(first.Err);
        Assert.Equal(first, Run("list", zip, "--format", format));
        Assert.DoesNotContain('\r', first.Out);
        if (format == "json")
        {
            using var json = JsonDocument.Parse(first.Out);
            Assert.Equal(Display(zip), json.RootElement.GetProperty("source").GetString());
            var directories = json.RootElement.GetProperty("directories");
            Assert.Equal([false, true], directories.EnumerateArray().Select(d => d.GetProperty("isBackup").GetBoolean()));
            Assert.Equal(["a.bin", "nested/b.bin", "z.bin"], directories[0].GetProperty("files").EnumerateArray().Select(f => f.GetProperty("path").GetString()));
        }
    }

    [Fact]
    public void DecoderList_Text_IsAnExactOrdinalLfTable()
    {
        const string expected = "ID    KIND      PLUGIN\nauto  built-in  —\njson  built-in  —\nnone  built-in  —\n";

        Assert.Equal(expected, Run("list-decoders").Out);
    }

    private static string Display(string path) => path.Replace(Path.DirectorySeparatorChar, '/');

    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr, () => new DecoderResolver([]));
        return (exit, stdout.ToString(), stderr.ToString());
    }
}
