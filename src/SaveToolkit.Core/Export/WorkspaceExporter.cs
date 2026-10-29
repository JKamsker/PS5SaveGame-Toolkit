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

public sealed class ExportException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>
/// Collects serialised output from dirty documents, verifies each by re-opening it with its plugin,
/// mirrors backups and writes a zip laid out exactly like the import.
/// </summary>
public static class WorkspaceExporter
{
    public static ExportPlan Plan(Workspace workspace, ExportOptions options)
        => Plan(workspace, options, CancellationToken.None);

    public static ExportPlan Plan(Workspace workspace, ExportOptions options, CancellationToken cancellationToken)
        => workspace.WithOwnershipLock(() => PlanLocked(workspace, options, cancellationToken), cancellationToken);

    private static ExportPlan PlanLocked(Workspace workspace, ExportOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var set = workspace.Set;
        var overrides = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.OrdinalIgnoreCase);
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dirty = workspace.Documents.Where(d => d.GetState(cancellationToken).IsDirty).ToList();
        var groups = workspace.DocumentGroups;
        foreach (var group in groups) group.CheckFingerprints(cancellationToken);

        foreach (var session in dirty)
        {
            foreach (var output in session.Serialize(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (groups.Any(g => g.Session.Id != session.Id && g.Owns(output.RelativePath)))
                    throw new ExportException($"'{output.RelativePath}' is owned by another document group.");
                if (((ISaveSet)set).FindFile(output.RelativePath) is null)
                {
                    throw new ExportException($"Plugin '{session.Plugin.Info.Id}' tried to write unknown file '{output.RelativePath}'.");
                }

                // Serialize already snapshots plugin-owned buffers under the session lock.
                if (!overrides.TryAdd(output.RelativePath, output.Content))
                {
                    throw new ExportException($"Two open documents both rewrite '{output.RelativePath}'. Close one of them.");
                }

                reasons[output.RelativePath] = $"edited with {session.Plugin.Info.Name}";
            }
        }

        if (options.MirrorBackups)
        {
            var groupPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in groups.SelectMany(group => group.Members))
            {
                cancellationToken.ThrowIfCancellationRequested();
                groupPaths.Add(member.RelativePath);
            }
            var protectedPaths = new HashSet<string>(groupPaths, StringComparer.OrdinalIgnoreCase);
            foreach (var document in workspace.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                protectedPaths.Add(document.File.RelativePath);
            }
            MirrorBackups(set, overrides, reasons, protectedPaths, groupPaths, cancellationToken);
        }

        // Mirrored siblings can participate in plugin/group invariants too. Validate the
        // complete overlay that will be exported, after every automatic override is added.
        Verify(workspace, dirty, overrides, cancellationToken);

        var included = Select(set, overrides, options.Scope, cancellationToken);
        var files = new List<ExportedFile>(included.Count);
        foreach (var file in included)
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(new ExportedFile(ExportPath(file),
                overrides.TryGetValue(file.RelativePath, out var content) ? content.Length : file.Length,
                reasons.GetValueOrDefault(file.RelativePath, "unchanged")));
        }
        return new ExportPlan(files, overrides, included);
    }

    public static void WriteZip(ExportPlan plan, Stream output) => WriteZip(plan, output, CancellationToken.None);

    public static void WriteZip(ExportPlan plan, Stream output, CancellationToken cancellationToken)
        => ExportZipWriter.Write(plan, output, cancellationToken);

    internal static string ExportPath(ISaveFile file) => file is DiskSaveFile disk ? disk.ExportRelativePath : file.RelativePath;

    private static void Verify(Workspace workspace, IReadOnlyList<DocumentSession> dirty, Dictionary<string, ReadOnlyMemory<byte>> overrides, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (overrides.Count == 0)
        {
            return;
        }

        var overlay = new OverlaySaveSet(workspace.Set, overrides, cancellationToken);
        foreach (var session in dirty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (workspace.DocumentGroups.FirstOrDefault(g => g.Session.Id == session.Id) is { } group)
            {
                try
                {
                    group.VerifyOutputs(overrides.Where(o => group.Owns(o.Key))
                        .ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase), overlay, cancellationToken);
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    throw new ExportException($"Group verification failed: {ex.Message}", ex);
                }
                continue;
            }
            var reopened = ((ISaveSet)overlay).FindFile(session.File.RelativePath)!;
            try
            {
                using var doc = session.Plugin.Open(reopened, workspace.References(session.Id));
                cancellationToken.ThrowIfCancellationRequested();
                _ = doc.BuildView();
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                throw new ExportException($"Verification failed: {session.Plugin.Info.Name} could not re-open its own output for '{session.File.RelativePath}': {ex.Message}", ex);
            }
        }
    }

    private static void MirrorBackups(DiskSaveSet set, Dictionary<string, ReadOnlyMemory<byte>> overrides,
        Dictionary<string, string> reasons, HashSet<string> protectedPaths, HashSet<string> groupPaths, CancellationToken cancellationToken)
    {
        foreach (var (path, content) in overrides.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Group backups are explicit members. Independent open backup sessions also retain ownership.
            if (groupPaths.Contains(path)) continue;
            if (((ISaveSet)set).FindFile(path) is not DiskSaveFile original || original.Directory.IsBackup)
            {
                continue;
            }

            var twin = set.Twin(original.Directory, cancellationToken)?.Files
                .OfType<DiskSaveFile>()
                .FirstOrDefault(f =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return string.Equals(f.PathInDirectory, original.PathInDirectory, StringComparison.OrdinalIgnoreCase);
                });
            if (twin is not null && twin.Sha256 == original.Sha256 && !overrides.ContainsKey(twin.RelativePath)
                && !protectedPaths.Contains(twin.RelativePath))
            {
                overrides[twin.RelativePath] = content;
                reasons[twin.RelativePath] = "mirrored to console backup";
            }
        }
    }

    private static List<ISaveFile> Select(DiskSaveSet set, Dictionary<string, ReadOnlyMemory<byte>> overrides,
        ExportScope scope, CancellationToken cancellationToken)
    {
        var included = new List<ISaveFile>();
        foreach (var directory in set.Directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = new List<ISaveFile>();
            var changed = false;
            foreach (var file in directory.SystemFiles.Concat(directory.Files))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rewritten = overrides.ContainsKey(file.RelativePath);
                changed |= rewritten;
                if (scope != ExportScope.ChangedFiles || rewritten) files.Add(file);
            }
            if (scope is ExportScope.Everything or ExportScope.ChangedFiles || changed) included.AddRange(files);
        }
        return included;
    }

    /// <summary>Hex SHA-256, handy for reports and tests.</summary>
    public static string Sha256(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
