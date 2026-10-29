using System.IO.Compression;
using System.Text;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;

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
}
