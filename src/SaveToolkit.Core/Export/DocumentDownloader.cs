using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Export;

/// <summary>Captures and verifies one independent document without planning a workspace export.</summary>
public static class DocumentDownloader
{
    /// <summary>
    /// Returns the verified primary output, or null to stream the unchanged imported primary file.
    /// Verification uses the same immutable snapshot returned here, including any sibling outputs.
    /// Neither verification nor download clears history or dirty state.
    /// </summary>
    public static FileOutput? Capture(Workspace workspace, DocumentSession session, CancellationToken cancellationToken = default)
        => workspace.WithOwnershipLock(() =>
        {
            if (!ReferenceEquals(workspace.FindDocument(session.Id), session))
                throw new KeyNotFoundException("Document not found. Open the file again.");
            if (workspace.DocumentGroups.Any(g => g.Session.Id == session.Id))
                throw new InvalidOperationException("Use workspace export to download all group members atomically.");

            // Serialize snapshots plugin-owned buffers under the session lock exactly once.
            // Later edits cannot change the captured bytes being reopened or returned.
            var outputs = session.Serialize(cancellationToken);
            var overrides = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.OrdinalIgnoreCase);
            DocumentOutputVerifier.AddOutputs(workspace, session, outputs, overrides, cancellationToken);
            DocumentOutputVerifier.Verify(workspace, [session], overrides, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return overrides.TryGetValue(session.File.RelativePath, out var primary)
                ? new FileOutput(session.File.RelativePath, primary) : null;
        }, cancellationToken);
}
