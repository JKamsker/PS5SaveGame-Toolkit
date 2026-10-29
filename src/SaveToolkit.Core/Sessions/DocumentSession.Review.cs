using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Sessions;

public sealed partial class DocumentSession
{
    private readonly SemanticReview _semantic;
    private readonly Func<ISaveDocument> _openOriginal;

    private void EnsureOriginal(CancellationToken cancellationToken = default) => _semantic.EnsureOriginal(_openOriginal, cancellationToken);

    public SemanticChangeLog GetChanges() => GetChanges(CancellationToken.None);

    public SemanticChangeLog GetChanges(CancellationToken cancellationToken) => Locked(() => RunPreflight(_ =>
    {
        // Publish learned host bindings only after the entire review succeeds. Plugin-side
        // registration is incidental; neither values nor session history are changed by a walk.
        var expanded = new List<string>();
        var log = _semantic.Query(_openOriginal, Document, field => _index.Add(field, cancellationToken), expanded.Add, _revision, cancellationToken);
        _expanded.UnionWith(expanded);
        return log;
    }, cancellationToken), cancellationToken);

    /// <summary>Restores one existing editable field to its original value; dependent values are recomputed by the plugin.</summary>
    public SessionEditResult RevertChange(string valueId, long expectedRevision) => RevertChange(valueId, expectedRevision, CancellationToken.None);

    public SessionEditResult RevertChange(string valueId, long expectedRevision, CancellationToken cancellationToken) => Locked(() => RunPreflight(_ =>
    {
        if (StaleCheck(expectedRevision, [valueId]) is { } stale) return stale;
        var log = GetChanges(cancellationToken);
        if (!log.Changes.Any(c => c.ValueId == valueId && c.CanRevert) || !_semantic.TryOriginal(valueId, out var original))
        {
            return Rejected(valueId, "This change cannot be reverted individually. Use undo for structural or derived changes.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return RunGroup(s => s.ApplyEdits([new IncomingEdit(valueId, JsonSerializer.SerializeToElement(original))], expectedRevision, cancellationToken));
    }, cancellationToken), cancellationToken);
}
