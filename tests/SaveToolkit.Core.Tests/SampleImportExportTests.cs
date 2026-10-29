using System.IO.Compression;
using System.Security.Cryptography;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class SampleImportExportTests
{
    public static TheoryData<string> Archives
    {
        get
        {
            var data = new TheoryData<string>();
            var archives = SampleSaves.ArtifactsDirectory is { } root
                ? Directory.GetFiles(root, "*_saves_dec*.zip").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()
                : [];
            foreach (var archive in archives) data.Add(archive!);
            if (data.Count == 0) data.Add(string.Empty);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Archives))]
    public void EverySampleEntrySurvivesImportExportWithIdenticalBytes(string archiveName)
    {
        Assert.SkipWhen(archiveName.Length == 0, "No external sample archives are available.");
        string archivePath = Path.Combine(SampleSaves.ArtifactsDirectory!, archiveName);
        using var store = new WorkspaceStore(new PluginCatalog([]),
            new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
        var workspace = store.Create(archiveName,
            [new ImportSource(archiveName, () => File.OpenRead(archivePath))]);
        var plan = WorkspaceExporter.Plan(workspace, new ExportOptions { Scope = ExportScope.Everything });
        Assert.Empty(plan.Overrides);
        foreach (var directory in workspace.Set.Directories)
        {
            string twinName = directory.IsBackup ? $"sdimg_{directory.Name}" : $"sdimg_sce_bu_{directory.Name}";
            var expected = directory.RawName.StartsWith("sdimg_", StringComparison.OrdinalIgnoreCase)
                ? workspace.Set.Directories.SingleOrDefault(other => other.Generation == directory.Generation
                    && string.Equals(other.TitleId, directory.TitleId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(other.RawName, twinName, StringComparison.OrdinalIgnoreCase))
                : null;
            Assert.Same(expected, workspace.Set.Twin(directory));
            if (expected is not null) Assert.Same(directory, workspace.Set.Twin(expected));
        }

        // Stream large dumps to task scratch, not memory or the checkout.
        string exportPath = Path.Combine(workspace.Set.StorageRoot, "roundtrip.zip");
        using (var output = File.Create(exportPath)) WorkspaceExporter.WriteZip(plan, output);
        using var original = ZipFile.OpenRead(archivePath);
        using var exported = ZipFile.OpenRead(exportPath);
        var actual = exported.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var entry in original.Entries)
        {
            string path = entry.FullName.Replace('\\', '/');
            if (path.EndsWith('/') || path.Split('/').Contains("__MACOSX", StringComparer.OrdinalIgnoreCase)) continue;
            var parsed = Assert.IsType<SaveSetImporter.ParsedPath>(SaveSetImporter.ParsePath(path));
            // The established export layout removes wrappers and makes appended generations explicit.
            string logicalPath = $"{parsed.TitleId}/{parsed.RawDirectory}/{parsed.PathInDirectory}";
            int generation = occurrences.GetValueOrDefault(logicalPath);
            occurrences[logicalPath] = generation + 1;
            string expectedPath = parsed.TitleId is null ? path : logicalPath;
            if (generation > 0) expectedPath = $"gen{generation}/{expectedPath}";
            Assert.True(actual.Remove(expectedPath, out var result), $"Missing exported entry: {expectedPath}");
            Assert.Equal(entry.Length, result!.Length);
            using var before = entry.Open();
            using var after = result.Open();
            Assert.Equal(SHA256.HashData(before), SHA256.HashData(after));
            count++;
        }
        Assert.Equal(count, plan.Files.Count);
        Assert.Empty(actual);
    }
}
