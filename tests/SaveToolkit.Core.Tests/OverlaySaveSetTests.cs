using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class OverlaySaveSetTests
{
    [Fact]
    public void FilesWithoutOverride_KeepOriginalContent()
    {
        var zip = TestZips.Build(
            ("PPSA00001/sdimg_SAVE/global.bin", [1, 2, 3]),
            ("PPSA00001/sdimg_SAVE/character.bin", [4, 5, 6, 7]));
        var set = SaveSetImporter.Import([new ImportSource("dump.zip", () => new MemoryStream(zip))], TestZips.TempDir());
        var global = set.AllDiskFiles.Single(f => f.FileName == "global.bin");

        ISaveSet overlay = new OverlaySaveSet(set, new Dictionary<string, ReadOnlyMemory<byte>> { [global.RelativePath] = new byte[] { 9 } });

        var files = overlay.Directories.Single().Files;
        var character = files.Single(f => f.FileName == "character.bin");
        Assert.Equal(4, character.Length);
        Assert.Equal([4, 5, 6, 7], character.ReadAllBytes());
        Assert.Equal([4, 5], character.ReadHeader(2));
        using (var stream = character.OpenRead())
        {
            Assert.Equal(4, stream.Length);
        }

        Assert.Equal([9], files.Single(f => f.FileName == "global.bin").ReadAllBytes());
    }

    [Fact]
    public void EmptyOverride_IsStillAnOverride()
    {
        var zip = TestZips.Build(("PPSA00001/sdimg_SAVE/data.bin", [1, 2, 3]));
        var set = SaveSetImporter.Import([new ImportSource("dump.zip", () => new MemoryStream(zip))], TestZips.TempDir());
        var file = set.AllDiskFiles.Single();

        ISaveSet overlay = new OverlaySaveSet(set, new Dictionary<string, ReadOnlyMemory<byte>> { [file.RelativePath] = ReadOnlyMemory<byte>.Empty });

        Assert.Equal(0, overlay.Directories.Single().Files.Single().Length);
    }
}
