using static SaveToolkit.SaveDiff.Tests.TestSaves;

namespace SaveToolkit.SaveDiff.Tests;

public sealed class CliArgumentTests
{
    [Theory]
    [InlineData("--file")]
    [InlineData("--title")]
    [InlineData("--ignore")]
    [InlineData("--semantic")]
    [InlineData("--decoder")]
    [InlineData("--gen")]
    [InlineData("--before-gen")]
    [InlineData("--after-gen")]
    [InlineData("--max-query-rows")]
    [InlineData("--max-query-pages")]
    [InlineData("--max-query-bytes")]
    [InlineData("--max-ranges")]
    [InlineData("--merge-gap")]
    [InlineData("--context")]
    [InlineData("--preview")]
    [InlineData("--max-changes")]
    [InlineData("--format")]
    [InlineData("--out")]
    public void Compare_OptionCannotConsumeTheNextOptionAsItsValue(string option)
    {
        var save = WriteZip(TempDir(), "a.zip", (Primary("data.bin"), [1]));

        var result = Run("compare", save, save, option, "--all");

        Assert.Equal(CliApp.ExitError, result.Exit);
        Assert.Empty(result.Out);
        Assert.Contains($"Option {option} needs a value.", result.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("compare", "a", "b", "--file")]
    [InlineData("list", "a", "--out")]
    [InlineData("list-decoders", "--format")]
    [InlineData("verify", "a", "--fixture")]
    [InlineData("verify", "a", "--artifacts", "--format", "json")]
    public void MissingOptionValues_ReturnUsageError(params string[] args)
    {
        var result = Run(args);

        Assert.Equal(CliApp.ExitError, result.Exit);
        Assert.Empty(result.Out);
        Assert.Contains("needs a value", result.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("compare", "--help")]
    [InlineData("list", "--help")]
    [InlineData("verify", "--help")]
    [InlineData("list-decoders", "--help")]
    [InlineData("compare", "-h")]
    [InlineData("list", "-h")]
    [InlineData("verify", "-h")]
    [InlineData("list-decoders", "-h")]
    public void Help_SucceedsWithoutAccessingInputsOrDiscoveringPlugins(params string[] args)
    {
        var result = Run(args);

        Assert.Equal(CliApp.ExitSame, result.Exit);
        Assert.Empty(result.Err);
        Assert.Contains("Exit codes:", result.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData()]
    [InlineData("compare")]
    [InlineData("compare", "one")]
    [InlineData("list")]
    [InlineData("verify")]
    [InlineData("list-decoders", "extra")]
    [InlineData("unknown", "--help")]
    public void MissingArgumentsAndUnknownCommands_ReturnTwo(params string[] args)
    {
        Assert.Equal(CliApp.ExitError, Run(args).Exit);
    }

    [Fact]
    public void Compare_RepeatedFileFiltersAndLastScalarValue_ArePreserved()
    {
        var dir = TempDir();
        var before = WriteZip(dir, "before.zip", (Primary("a.bin"), [1]), (Primary("b.bin"), [2]));
        var after = WriteZip(dir, "after.zip", (Primary("a.bin"), [3]), (Primary("b.bin"), [4]));

        var result = Run("compare", before, after, "--file", "a.bin", "--file", "b.bin", "--format", "text", "--format", "json");

        Assert.Equal(CliApp.ExitDifferent, result.Exit);
        Assert.Empty(result.Err);
        Assert.StartsWith("{", result.Out, StringComparison.Ordinal);
        Assert.Contains("a.bin", result.Out, StringComparison.Ordinal);
        Assert.Contains("b.bin", result.Out, StringComparison.Ordinal);
    }

    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr, () => throw new InvalidOperationException("Unexpected decoder discovery."));
        return (exit, stdout.ToString(), stderr.ToString());
    }
}
