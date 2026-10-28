using System.IO.Compression;
using System.Security.Cryptography;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Export;

public enum ExportScope
{
    /// <summary>Only rewritten files (plus mirrored backups).</summary>
    ChangedFiles,

    /// <summary>Complete save directories (incl. sce_sys) that contain a rewritten file. Ready to re-import with a save manager.</summary>
    ChangedDirectories,

    /// <summary>The whole imported set.</summary>
    Everything,
}

public sealed record ExportOptions
{
    public ExportScope Scope { get; init; } = ExportScope.ChangedDirectories;

    /// <summary>Also replace the <c>sdimg_sce_bu_*</c> twin when it held the same original bytes.</summary>
    public bool MirrorBackups { get; init; } = true;
}

public sealed record ExportedFile(string RelativePath, long Length, string Reason);

public sealed record ExportPlan(IReadOnlyList<ExportedFile> Files, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Overrides, IReadOnlyList<ISaveFile> Included);

public sealed class ExportException(string message) : Exception(message);

/// <summary>
/// Collects serialised output from dirty documents, verifies each by re-opening it with its plugin,
/// mirrors backups and writes a zip laid out exactly like the import.
/// </summary>
public static class WorkspaceExporter
{
    public static ExportPlan Plan(Workspace workspace, ExportOptions options)
    {
        var set = workspace.Set;
        var overrides = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.OrdinalIgnoreCase);
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dirty = workspace.Documents.Where(d => d.State.IsDirty).ToList();

        foreach (var session in dirty)
        {
            foreach (var output in session.Serialize())
            {
                if (((ISaveSet)set).FindFile(output.RelativePath) is null)
                {
                    throw new ExportException($"Plugin '{session.Plugin.Info.Id}' tried to write unknown file '{output.RelativePath}'.");
                }

                if (!overrides.TryAdd(output.RelativePath, output.Content))
                {
                    throw new ExportException($"Two open documents both rewrite '{output.RelativePath}'. Close one of them.");
                }

                reasons[output.RelativePath] = $"edited with {session.Plugin.Info.Name}";
            }
        }

        Verify(set, dirty, overrides);

        if (options.MirrorBackups)
        {
            MirrorBackups(set, overrides, reasons);
        }

        var included = Select(set, overrides, options.Scope);
        var files = included
            .Select(f => new ExportedFile(
                f.RelativePath,
                overrides.TryGetValue(f.RelativePath, out var c) ? c.Length : f.Length,
                reasons.GetValueOrDefault(f.RelativePath, "unchanged")))
            .ToList();
        return new ExportPlan(files, overrides, included);
    }

    public static void WriteZip(ExportPlan plan, Stream output)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var file in plan.Included)
        {
            var entry = zip.CreateEntry(file.RelativePath, CompressionLevel.Fastest);
            using var target = entry.Open();
            if (plan.Overrides.TryGetValue(file.RelativePath, out var content))
            {
                target.Write(content.Span);
            }
            else
            {
                using var source = file.OpenRead();
                source.CopyTo(target);
            }
        }
    }

    private static void Verify(DiskSaveSet set, IReadOnlyList<DocumentSession> dirty, Dictionary<string, ReadOnlyMemory<byte>> overrides)
    {
        if (overrides.Count == 0)
        {
            return;
        }

        var overlay = new OverlaySaveSet(set, overrides);
        foreach (var session in dirty)
        {
            var reopened = ((ISaveSet)overlay).FindFile(session.File.RelativePath)!;
            try
            {
                using var doc = session.Plugin.Open(reopened);
                _ = doc.BuildView();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new ExportException($"Verification failed: {session.Plugin.Info.Name} could not re-open its own output for '{session.File.RelativePath}': {ex.Message}");
            }
        }
    }

    private static void MirrorBackups(DiskSaveSet set, Dictionary<string, ReadOnlyMemory<byte>> overrides, Dictionary<string, string> reasons)
    {
        foreach (var (path, content) in overrides.ToList())
        {
            if (((ISaveSet)set).FindFile(path) is not DiskSaveFile original || original.Directory.IsBackup)
            {
                continue;
            }

            var twin = set.Twin(original.Directory)?.Files
                .OfType<DiskSaveFile>()
                .FirstOrDefault(f => string.Equals(f.PathInDirectory, original.PathInDirectory, StringComparison.OrdinalIgnoreCase));
            if (twin is not null && twin.Sha256 == original.Sha256 && !overrides.ContainsKey(twin.RelativePath))
            {
                overrides[twin.RelativePath] = content;
                reasons[twin.RelativePath] = "mirrored to console backup";
            }
        }
    }

    private static List<ISaveFile> Select(DiskSaveSet set, Dictionary<string, ReadOnlyMemory<byte>> overrides, ExportScope scope)
    {
        IEnumerable<ISaveFile> all = set.Directories.SelectMany(d => d.SystemFiles.Concat(d.Files));
        return scope switch
        {
            ExportScope.Everything => [.. all],
            ExportScope.ChangedFiles => [.. all.Where(f => overrides.ContainsKey(f.RelativePath))],
            _ => [.. set.Directories
                .Where(d => d.Files.Concat(d.SystemFiles).Any(f => overrides.ContainsKey(f.RelativePath)))
                .SelectMany(d => d.SystemFiles.Concat(d.Files))],
        };
    }

    /// <summary>Hex SHA-256, handy for reports and tests.</summary>
    public static string Sha256(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
