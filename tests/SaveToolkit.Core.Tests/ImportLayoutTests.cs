using SaveToolkit.Core.Storage;

namespace SaveToolkit.Core.Tests;

public sealed class ImportLayoutTests
{
    [Theory]
    [InlineData("./PPSA00001/sdimg_SAVE/sub/data.bin", "sdimg_SAVE", "sub/data.bin")]
    [InlineData("wrapper/PPSA00001/SAVE/sub/data.bin", "SAVE", "sub/data.bin")]
    [InlineData("wrapper/PPSA00001/extra/sdimg_SAVE/sub/data.bin", "sdimg_SAVE", "sub/data.bin")]
    [InlineData("SAVE/sub/data.bin", "SAVE", "sub/data.bin")]
    [InlineData(".\\PPSA00001\\sdimg_SAVE\\sub\\data.bin", "sdimg_SAVE", "sub/data.bin")]
    [InlineData("PPSA00001//sdimg_SAVE/./sub/data.bin", "sdimg_SAVE", "sub/data.bin")]
    public void Preserves_nested_paths_and_normalizes_zip_tool_prefixes(string path, string raw, string relative)
    {
        var set = Import(TestZips.Build((path, [1, 2, 3])));
        var directory = Assert.Single(set.Directories);
        Assert.Equal(raw, directory.RawName);
        var file = Assert.Single(directory.Files);
        Assert.Equal(relative, file.PathInDirectory);
        Assert.Equal([1, 2, 3], file.ReadAllBytes());
        Assert.Equal(path.Contains("PPSA00001", StringComparison.Ordinal) ? "PPSA00001" : "UNKNOWN", directory.TitleId);
    }

    [Fact]
    public void Case_variants_share_one_directory_and_duplicate_files_keep_generations()
    {
        var set = Import(TestZips.Build(
            ("PPSA00001/sdimg_SAVE/a.bin", [1]),
            ("ppsa00001/SDIMG_save/b.bin", [2]),
            ("PPSA00001/SDIMG_SAVE/A.BIN", [3]),
            ("PPSA00001/sdimg_save/B.BIN", [4])));

        Assert.Equal(2, set.Directories.Count);
        foreach (var directory in set.Directories)
        {
            Assert.Equal("sdimg_SAVE", directory.RawName);
            Assert.Equal(2, directory.Files.Count);
        }
        Assert.Equal([1], set.FindFile("PPSA00001/sdimg_SAVE/a.bin")!.ReadAllBytes());
        Assert.Equal([2], set.FindFile("PPSA00001/sdimg_SAVE/b.bin")!.ReadAllBytes());
        Assert.Equal([3], set.FindFile("gen1/PPSA00001/sdimg_SAVE/a.bin")!.ReadAllBytes());
        Assert.Equal([4], set.FindFile("gen1/PPSA00001/sdimg_SAVE/b.bin")!.ReadAllBytes());
    }

    [Fact]
    public void Directory_markers_and_nested_macos_metadata_are_not_save_files()
    {
        var set = Import(TestZips.Build(
            ("PPSA00001/sdimg_SAVE/", []),
            ("PPSA00001\\sdimg_SAVE\\", []),
            ("PPSA00001/sdimg_SAVE/subfolder\\", []),
            ("wrapper/__MACOSX/PPSA00001/sdimg_SAVE/._data.bin", [9]),
            ("PPSA00001/sdimg_SAVE/data.bin", [])));

        var file = Assert.Single(Assert.Single(set.Directories).Files);
        Assert.Equal("data.bin", file.PathInDirectory);
        Assert.Equal(0, file.Length);
        Assert.Empty(file.ReadAllBytes());
    }

    [Theory]
    [InlineData(256, "x")]
    [InlineData(4, "?")]
    public void Unsupported_names_reject_the_upload_instead_of_silently_losing_files(int length, string character)
    {
        string name = new(character[0], length);
        var archive = TestZips.Build(("good.bin", [1]), ($"PPSA00001/sdimg_SAVE/{name}", [2]));
        var error = Assert.Throws<ImportException>(() => Import(archive));
        Assert.Contains(name, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unicode_and_long_logical_paths_preserve_names_and_bytes()
    {
        string relative = string.Join('/', Enumerable.Repeat(new string('界', 200), 12)) + "/保存🎮.bin";
        var set = Import(TestZips.Build(($"PPSA00001/sdimg_保存/{relative}", [7, 8])));
        var file = Assert.Single(Assert.Single(set.Directories).Files);
        Assert.Equal(relative, file.PathInDirectory);
        Assert.Equal([7, 8], file.ReadAllBytes());
    }

    [Theory]
    [InlineData("")]
    [InlineData("PPSA00001/")]
    public void Save_folder_contents_keep_root_system_files_beside_game_data(string prefix)
    {
        var set = Import(TestZips.Build(
            ($"{prefix}data.bin", [1]),
            ($"{prefix}sce_sys/param.sfo", [2]),
            ($"{prefix}sce_sys/icon0.png", [])));

        var directory = Assert.Single(set.Directories);
        Assert.Equal("loose", directory.RawName);
        Assert.Equal("data.bin", Assert.Single(directory.Files).PathInDirectory);
        Assert.Equal(2, directory.SystemFiles.Count);
        Assert.Contains(directory.SystemFiles, f => f.PathInDirectory == "sce_sys/param.sfo");
        Assert.Contains(directory.SystemFiles, f => f.PathInDirectory == "sce_sys/icon0.png");
    }

    private static DiskSaveSet Import(byte[] archive) => SaveSetImporter.Import(
        [new ImportSource("dump.zip", () => new MemoryStream(archive))], TestZips.TempDir());
}
