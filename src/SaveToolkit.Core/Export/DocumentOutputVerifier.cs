using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Export;

/// <summary>Verifies captured outputs in their complete staged save set, using pinned references.</summary>
internal static class DocumentOutputVerifier
{
    public static void CheckFingerprints(DocumentSession session, CancellationToken cancellationToken)
    {
        try { session.CheckOutputFingerprints(cancellationToken); }
        catch (Exception ex) when (ex is InvalidOperationException or SaveFormatException or IOException)
        {
            throw new ExportException(ex.Message, ex);
        }
    }

    public static void AddOutputs(Workspace workspace, DocumentSession session, DocumentOutputCapture capture,
        Dictionary<string, ReadOnlyMemory<byte>> overrides, CancellationToken cancellationToken)
    {
        var outputs = capture.Outputs;
        CheckFingerprints(session, cancellationToken);
        var pending = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.OrdinalIgnoreCase);
        var groups = workspace.DocumentGroups;
        var grouped = groups.Any(group => group.Session.Id == session.Id);
        foreach (var output in outputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (groups.Any(g => g.Session.Id != session.Id && g.Owns(output.RelativePath)))
                throw new ExportException($"'{output.RelativePath}' is owned by another document group.");
            var file = ((ISaveSet)workspace.Set).FindAnyFile(output.RelativePath);
            if (file is null)
                throw new ExportException($"Plugin '{session.Plugin.Info.Id}' tried to write unknown file '{output.RelativePath}'.");
            if (file.PathInDirectory.StartsWith("sce_sys/", StringComparison.OrdinalIgnoreCase)
                && !session.OwnedOutputPaths.Contains(output.RelativePath, StringComparer.OrdinalIgnoreCase))
                throw new ExportException($"Plugin '{session.Plugin.Info.Id}' tried to write undeclared system file '{output.RelativePath}'.");
            if (overrides.ContainsKey(output.RelativePath) || !pending.TryAdd(output.RelativePath, output.Content))
                throw new ExportException($"Two open documents both rewrite '{output.RelativePath}'. Close one of them.");
        }

        if (session.RequiresCompleteOutputs && !grouped && capture.IsDirty)
        {
            foreach (var expected in session.OwnedOutputPaths)
            {
                if (!pending.ContainsKey(expected))
                    throw new ExportException($"Plugin '{session.Plugin.Info.Id}' did not serialize required output '{expected}'.");
            }
            if (pending.Keys.Any(path => !session.OwnedOutputPaths.Contains(path, StringComparer.OrdinalIgnoreCase)))
                throw new ExportException($"Plugin '{session.Plugin.Info.Id}' returned an output outside its declared ownership set.");
        }

        foreach (var (path, content) in pending)
            overrides.Add(path, content);
    }

    public static void Verify(Workspace workspace, IReadOnlyList<DocumentSession> sessions,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> overrides, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (overrides.Count == 0) return;

        OverlaySaveSet overlay;
        try
        {
            overlay = new OverlaySaveSet(workspace.Set, overrides, cancellationToken);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException or ExportException))
        {
            throw new ExportException($"Overlay verification failed: {ex.Message}", ex);
        }
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
