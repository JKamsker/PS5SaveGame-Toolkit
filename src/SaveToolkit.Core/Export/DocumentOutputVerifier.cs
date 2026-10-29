using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Export;

/// <summary>Verifies captured outputs in their complete staged save set, using pinned references.</summary>
internal static class DocumentOutputVerifier
{
    public static void AddOutputs(Workspace workspace, DocumentSession session, IReadOnlyList<FileOutput> outputs,
        Dictionary<string, ReadOnlyMemory<byte>> overrides, CancellationToken cancellationToken)
    {
        var groups = workspace.DocumentGroups;
        foreach (var output in outputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (groups.Any(g => g.Session.Id != session.Id && g.Owns(output.RelativePath)))
                throw new ExportException($"'{output.RelativePath}' is owned by another document group.");
            if (((ISaveSet)workspace.Set).FindFile(output.RelativePath) is null)
                throw new ExportException($"Plugin '{session.Plugin.Info.Id}' tried to write unknown file '{output.RelativePath}'.");
            if (!overrides.TryAdd(output.RelativePath, output.Content))
                throw new ExportException($"Two open documents both rewrite '{output.RelativePath}'. Close one of them.");
        }
    }

    public static void Verify(Workspace workspace, IReadOnlyList<DocumentSession> sessions,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> overrides, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (overrides.Count == 0) return;

        var overlay = new OverlaySaveSet(workspace.Set, overrides, cancellationToken);
        foreach (var session in sessions)
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
}
