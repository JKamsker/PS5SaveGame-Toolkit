using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherSampleSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AvailableArchiveWithoutGameplaySavesFailsInsteadOfPassingAnEmptyLoop(bool sidecarOnly)
    {
        ISaveFile[] files = sidecarOnly ? [MemorySaveSet.SingleFile("Manual.req", [1])] : [];
        Assert.Throws<Xunit.Sdk.NotEmptyException>(() => EnabledWriteFixture.SelectSaves(files));
    }

    [Fact]
    public void SelectionRetainsEverySaveIncludingBackupTwinsAndExcludesSidecars()
    {
        var set = new MemorySaveSet()
            .With("PPSA04018", "TEST", new Dictionary<string, byte[]> { ["Manual.sav"] = [1], ["Manual.req"] = [2] })
            .With("PPSA04018", "TEST", new Dictionary<string, byte[]> { ["Manual.sav"] = [3] }, backup: true);
        var files = set.Directories.SelectMany(d => d.Files).ToArray();
        var selected = EnabledWriteFixture.SelectSaves(files);
        Assert.Equal(files.Where(f => f.FileName == "Manual.sav"), selected);
        Assert.Equal([false, true], selected.Select(f => f.Directory.IsBackup));
        Assert.Equal(new byte[] { 1, 3 }, selected.Select(f => Assert.Single(f.ReadAllBytes())));
    }
}
