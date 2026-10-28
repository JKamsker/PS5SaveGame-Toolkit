using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class ImporterTests
{
    [Theory]
    [InlineData("PPSA02530/sdimg_SAVE/data.bin", "PPSA02530", "sdimg_SAVE", "data.bin")]
    [InlineData("x/PPSA02530/sdimg_sce_bu_SAVE/sce_sys/param.sfo", "PPSA02530", "sdimg_sce_bu_SAVE", "sce_sys/param.sfo")]
    [InlineData("sdimg_SAVE/data.bin", null, "sdimg_SAVE", "data.bin")]
    [InlineData("ppsa01234\\SAVE\\data.bin", "PPSA01234", "SAVE", "data.bin")]
    [InlineData("data.bin", null, "loose", "data.bin")]
    public void ParsePath_SplitsLayout(string path, string? title, string dir, string inDir)
    {
        var parsed = SaveSetImporter.ParsePath(path);

        Assert.NotNull(parsed);
        Assert.Equal(title, parsed.TitleId);
        Assert.Equal(dir, parsed.RawDirectory);
        Assert.Equal(inDir, parsed.PathInDirectory);
    }

    [Theory]
    [InlineData("PPSA02530/sdimg_SAVE/../../evil.bin")]
    [InlineData("__MACOSX/PPSA02530/sdimg_SAVE/x")]
    [InlineData("PPSA02530/sdimg_SAVE/")]
    [InlineData("C:/PPSA02530/sdimg_SAVE/x")]
    public void ParsePath_RejectsUnsafeOrEmpty(string path) => Assert.Null(SaveSetImporter.ParsePath(path));

    [Fact]
    public void Import_SplitsRepeatedPathsIntoGenerations_AndPairsBackups()
    {
        var zip = TestZips.Build(
            ("PPSA00001/sdimg_SAVE/data.bin", [1, 2, 3]),
            ("PPSA00001/sdimg_sce_bu_SAVE/data.bin", [1, 2, 3]),
            ("PPSA00001/sdimg_SAVE/data.bin", [9, 9]));

        var set = SaveSetImporter.Import([new ImportSource("dump.zip", () => new MemoryStream(zip))], TestZips.TempDir());

        Assert.Equal(3, set.Directories.Count);
        var primary = set.Directories.Single(d => d is { Generation: 0, IsBackup: false });
        var backup = set.Directories.Single(d => d.IsBackup);
        var second = set.Directories.Single(d => d.Generation == 1);
        Assert.Equal("SAVE", primary.Name);
        Assert.Same(backup, set.Twin(primary));
        Assert.Equal([9, 9], second.Files[0].ReadAllBytes());
        Assert.Equal("gen1/PPSA00001/sdimg_SAVE/data.bin", second.Files[0].RelativePath);
        Assert.Equal("PPSA00001/sdimg_SAVE/data.bin", primary.Files[0].RelativePath);
        Assert.NotNull(((ISaveSet)set).FindFile("PPSA00001/sdimg_sce_bu_SAVE/data.bin"));
    }

    [Fact]
    public void Import_SeparatesSystemFiles()
    {
        var zip = TestZips.Build(("PPSA00001/sdimg_SAVE/sce_sys/keystone", [0]), ("PPSA00001/sdimg_SAVE/data.bin", [1]));

        var set = SaveSetImporter.Import([new ImportSource("dump.zip", () => new MemoryStream(zip))], TestZips.TempDir());

        var dir = set.Directories.Single();
        Assert.Equal("data.bin", Assert.Single(dir.Files).PathInDirectory);
        Assert.Equal("sce_sys/keystone", Assert.Single(dir.SystemFiles).PathInDirectory);
    }

    [Fact]
    public void Import_EnforcesSizeLimit()
    {
        var zip = TestZips.Build(("PPSA00001/sdimg_SAVE/data.bin", new byte[2048]));

        Assert.Throws<ImportException>(() => SaveSetImporter.Import(
            [new ImportSource("dump.zip", () => new MemoryStream(zip))],
            TestZips.TempDir(),
            new ImportLimits { MaxTotalBytes = 1024 }));
    }

    [Fact]
    public void Import_WithoutFiles_Throws()
    {
        var zip = TestZips.Build();

        Assert.Throws<ImportException>(() => SaveSetImporter.Import([new ImportSource("dump.zip", () => new MemoryStream(zip))], TestZips.TempDir()));
    }
}
