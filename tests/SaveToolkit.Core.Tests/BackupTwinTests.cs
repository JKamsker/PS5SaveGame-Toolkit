using System.IO.Compression;
using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class BackupTwinTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConsolePairIsSymmetricRegardlessOfLooseCollisionOrImportOrder(bool collision, bool reverse)
    {
        var entries = Entries(collision);
        var set = Import(reverse ? entries.Reverse().ToArray() : entries);
        var primary = set.Directories.Single(d => d.RawName == "sdimg_SAVE");
        var backup = set.Directories.Single(d => d.RawName == "sdimg_sce_bu_SAVE");

        Assert.Same(backup, set.Twin(primary));
        Assert.Same(primary, set.Twin(backup));
        if (collision) Assert.Null(set.Twin(set.Directories.Single(d => d.RawName == "SAVE")));
        Assert.Equal(entries.Length, set.AllDiskFiles.Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LooseDirectoryCannotSubstituteForAMissingConsolePrimary(bool reverse)
    {
        (string Path, byte[] Content)[] entries =
        [
            ("PPSA99999/SAVE/save.dat", CounterPlugin.Create(5)),
            ("PPSA99999/sdimg_sce_bu_SAVE/save.dat", CounterPlugin.Create(5)),
        ];
        var set = Import(reverse ? entries.Reverse().ToArray() : entries);

        Assert.All(set.Directories, directory => Assert.Null(set.Twin(directory)));
        Assert.Equal(2, set.AllDiskFiles.Count());
    }

    [Fact]
    public void PairingKeepsTitleGenerationAndLogicalNameBoundaries()
    {
        var set = Import(
            ("PPSA99999/sdimg_SAVE/save.dat", [1]),
            ("PPSA99999/sdimg_sce_bu_SAVE/save.dat", [1]),
            ("PPSA99999/sdimg_SAVE/save.dat", [2]),
            ("PPSA00001/sdimg_sce_bu_SAVE/save.dat", [3]),
            ("PPSA99999/sdimg_sce_bu_OTHER/save.dat", [4]));

        Assert.All(set.Directories.Where(d => d.Generation > 0 || d.TitleId == "PPSA00001" || d.Name == "OTHER"),
            directory => Assert.Null(set.Twin(directory)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExportMirrorsOnlyTheConsolePrimaryAndPreservesEveryOtherFile(bool editLoose, bool reverse)
    {
        // Equal originals ensure a wrong twin would be overwritten, rather than hidden by the hash guard.
        var entries = Entries(collision: true);
        byte[] input = TestZips.Build(reverse ? entries.Reverse().ToArray() : entries);
        using var store = new WorkspaceStore(new PluginCatalog([new CounterPlugin()]),
            new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
        var workspace = store.Create("twin collision", [new ImportSource("dump.zip", () => new MemoryStream(input))]);
        string editedDirectory = editLoose ? "SAVE" : "sdimg_SAVE";
        var file = workspace.Set.AllDiskFiles.Single(f => f.Directory.RawName == editedDirectory);
        var session = workspace.Open(file.Id, null);
        _ = session.BuildView();
        using var value = JsonDocument.Parse("42");
        Assert.True(session.ApplyEdits([new IncomingEdit("counter", value.RootElement)]).Ok);

        var plan = WorkspaceExporter.Plan(workspace, new ExportOptions { Scope = ExportScope.Everything });
        Assert.Equal(editLoose ? 1 : 2, plan.Overrides.Count);
        using var output = new MemoryStream();
        WorkspaceExporter.WriteZip(plan, output);
        output.Position = 0;
        using var archive = new ZipArchive(output, ZipArchiveMode.Read);
        Assert.Equal(entries.Length, archive.Entries.Count);
        foreach (var entry in entries)
        {
            bool edited = entry.Path == file.RelativePath
                || (!editLoose && entry.Path.Contains("/sdimg_sce_bu_", StringComparison.Ordinal));
            using var source = Assert.IsType<ZipArchiveEntry>(archive.GetEntry(entry.Path)).Open();
            using var content = new MemoryStream();
            source.CopyTo(content);
            Assert.Equal(edited ? CounterPlugin.Create(42) : entry.Content, content.ToArray());
        }
    }

    private static (string Path, byte[] Content)[] Entries(bool collision)
    {
        List<(string, byte[])> entries =
        [
            ("PPSA99999/sdimg_SAVE/save.dat", CounterPlugin.Create(5)),
            ("PPSA99999/sdimg_sce_bu_SAVE/save.dat", CounterPlugin.Create(5)),
        ];
        if (collision) entries.Add(("PPSA99999/SAVE/save.dat", CounterPlugin.Create(5)));
        return entries.ToArray();
    }

    private static DiskSaveSet Import(params (string Path, byte[] Content)[] entries)
    {
        byte[] zip = TestZips.Build(entries);
        return SaveSetImporter.Import([new ImportSource("dump.zip", () => new MemoryStream(zip))], TestZips.TempDir());
    }
}
