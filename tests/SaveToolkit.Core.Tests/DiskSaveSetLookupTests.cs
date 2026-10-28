using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class DiskSaveSetLookupTests : IDisposable
{
    private readonly WorkspaceStore _store = new(new PluginCatalog([]), new() { DataRoot = TestZips.TempDir() });
    public void Dispose() => _store.Dispose();

    [Fact]
    public void FindFile_PreservesCaseInsensitiveGameFileLookupAcrossGenerations()
    {
        const string path = "PPSA00001/sdimg_SAVE/data.bin";
        var zip = TestZips.Build((path, [1]), (path, [2]), ("PPSA00001/sdimg_SAVE/sce_sys/param.sfo", [0]));
        var set = _store.Create("lookup", [new ImportSource("dump.zip", () => new MemoryStream(zip))]).Set;
        ISaveSet contract = set;

        foreach (var file in set.AllDiskFiles.Where(f => !f.IsSystemFile))
        {
            Assert.Same(file, contract.FindFile(file.RelativePath.ToUpperInvariant()));
            Assert.Same(file, set.FindById(file.Id));
        }

        Assert.Equal([1], contract.FindFile(path)!.ReadAllBytes());
        Assert.Equal([2], contract.FindFile("gen1/" + path)!.ReadAllBytes());
        Assert.Null(contract.FindFile("PPSA00001/sdimg_SAVE/missing.bin"));
        Assert.Null(contract.FindFile("PPSA00001/sdimg_SAVE/sce_sys/param.sfo"));
    }
}
