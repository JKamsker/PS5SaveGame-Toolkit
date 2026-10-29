using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class ExportLayoutTests
{
    public static TheoryData<string[]> ImportedLayouts { get; } = new()
    {
        new[] { "unrecognized-layout/mystery.dat" },
        new[] { "mystery.dat", "empty.dat", "sce_sys/param.sfo" },
        new[] { "PPSA00001/sdimg_SAVE/save.dat", "PPSA00001/sdimg_SAVE/sce_sys/param.sfo",
            "unrecognized-layout/sub/mystery.dat", "root.dat" },
        new[] { "unrecognized-layout/mystery.dat", "unrecognized-layout/MYSTERY.DAT",
            "UNRECOGNIZED-LAYOUT/other.dat", "Mystery.dat", "mystery.dat" },
        new[] { "保存🎮/未知.dat", "保存🎮/空.dat" },
        new[] { "sdimg_SAVE/mystery.dat", "sdimg_SAVE/sce_sys/param.sfo" },
        new[] { "unrecognized-layout/mystery.dat", "unrecognized-layout/mystery.dat" },
    };

    [Theory]
    [MemberData(nameof(ImportedLayouts))]
    public void Whole_dump_without_edits_preserves_imported_entry_names_and_payloads(string[] paths)
    {
        var entries = paths.Select((path, index) => (Path: path,
            Content: index == 1 ? Array.Empty<byte>() : Encoding.UTF8.GetBytes($"Unknown save content {index}"))).ToArray();
        byte[] input = TestZips.Build(entries);
        using var store = new WorkspaceStore(new PluginCatalog([]),
            new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
        var workspace = store.Create("round trip", [new ImportSource("dump.zip", () => new MemoryStream(input))]);

        var plan = WorkspaceExporter.Plan(workspace, new ExportOptions { Scope = ExportScope.Everything });
        using var output = new MemoryStream();
        WorkspaceExporter.WriteZip(plan, output);
        output.Position = 0;
        using var exported = new ZipArchive(output, ZipArchiveMode.Read);

        // Compare names as UTF-8 bytes, including case, independently of ZIP entry order.
        var expected = entries.OrderBy(e => e.Path, StringComparer.Ordinal).ToArray();
        var actual = exported.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected.Select(e => e.Path), plan.Files.Select(f => f.RelativePath).Order(StringComparer.Ordinal));
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(Encoding.UTF8.GetBytes(expected[index].Path), Encoding.UTF8.GetBytes(actual[index].FullName));
            using var content = new MemoryStream();
            using var source = actual[index].Open();
            source.CopyTo(content);
            Assert.Equal(expected[index].Content, content.ToArray());
        }
        Assert.Empty(plan.Overrides);
    }

    [Theory]
    [InlineData(ExportScope.ChangedFiles)]
    [InlineData(ExportScope.ChangedDirectories)]
    [InlineData(ExportScope.Everything)]
    public void Edited_unknown_layout_uses_logical_paths_for_overrides_and_imported_paths_for_export(ExportScope scope)
    {
        const string primary = "unrecognized-layout/save.dat";
        const string caseVariant = "unrecognized-layout/SAVE.DAT";
        const string system = "unrecognized-layout/sce_sys/param.sfo";
        byte[] input = TestZips.Build((primary, CounterPlugin.Create(5)),
            (caseVariant, CounterPlugin.Create(9)), (system, [0]));
        using var store = new WorkspaceStore(new PluginCatalog([new CounterPlugin()]),
            new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
        var workspace = store.Create("edited", [new ImportSource("dump.zip", () => new MemoryStream(input))]);
        var file = workspace.Set.AllDiskFiles.Single(f => f.PathInDirectory == "save.dat");
        var session = workspace.Open(file.Id, null);
        _ = session.BuildView();
        using var value = JsonDocument.Parse("42");
        Assert.True(session.ApplyEdits([new IncomingEdit("counter", value.RootElement)]).Ok);

        var plan = WorkspaceExporter.Plan(workspace, new ExportOptions { Scope = scope });
        Assert.Equal(CounterPlugin.Create(42), plan.Overrides[file.RelativePath].ToArray());
        using var output = new MemoryStream();
        WorkspaceExporter.WriteZip(plan, output);
        output.Position = 0;
        using var zip = new ZipArchive(output, ZipArchiveMode.Read);
        Assert.Equal(scope == ExportScope.Everything ? 3 : scope == ExportScope.ChangedDirectories ? 2 : 1, zip.Entries.Count);
        Assert.Equal(plan.Files.Select(f => f.RelativePath), zip.Entries.Select(e => e.FullName));
        using var edited = new MemoryStream();
        using (var source = zip.GetEntry(primary)!.Open()) source.CopyTo(edited);
        Assert.Equal(CounterPlugin.Create(42), edited.ToArray());
        if (scope == ExportScope.Everything)
        {
            using var untouched = new MemoryStream();
            using (var source = zip.GetEntry(caseVariant)!.Open()) source.CopyTo(untouched);
            Assert.Equal(CounterPlugin.Create(9), untouched.ToArray());
        }
        if (scope != ExportScope.ChangedFiles) Assert.NotNull(zip.GetEntry(system));
    }

    [Fact]
    public void Known_layout_keeps_existing_wrapper_normalization_and_generation_paths()
    {
        byte[] input = TestZips.Build(("wrapper/PPSA00001/sdimg_SAVE/save.dat", [1]),
            ("wrapper/PPSA00001/sdimg_SAVE/save.dat", [2]));
        using var store = new WorkspaceStore(new PluginCatalog([]),
            new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
        var workspace = store.Create("known layout", [new ImportSource("dump.zip", () => new MemoryStream(input))]);

        var plan = WorkspaceExporter.Plan(workspace, new ExportOptions { Scope = ExportScope.Everything });

        Assert.Equal(["PPSA00001/sdimg_SAVE/save.dat", "gen1/PPSA00001/sdimg_SAVE/save.dat"],
            plan.Files.Select(f => f.RelativePath));
    }
}
