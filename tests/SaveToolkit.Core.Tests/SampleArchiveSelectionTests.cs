using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class SampleArchiveSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualLengthArchives_UseOrdinalNamesRegardlessOfEnumerationOrder(bool reverse)
    {
        string[] names = ["PPSA00000_saves_dec_b.zip", "PPSA00000_saves_dec_a.zip"];

        Assert.Equal(names[1], SampleSaves.SelectArchive(reverse ? names.Reverse() : names));
    }

    [Fact]
    public void ShortestArchiveStillWins_AndOtherTitlesAreIgnored()
    {
        var root = TestZips.TempDir();
        Directory.CreateDirectory(root);
        string[] names = ["PPSA00000_saves_dec_a.zip", "PPSA00000_saves_dec.zip", "PPSA11111_saves_dec.zip"];
        foreach (var name in names) File.WriteAllBytes(Path.Combine(root, name), []);

        Assert.Equal(Path.Combine(root, names[1]), SampleSaves.FindArchive(root, "PPSA00000"));
        Assert.Null(SampleSaves.FindArchive(root, "PPSA22222"));
    }
}
