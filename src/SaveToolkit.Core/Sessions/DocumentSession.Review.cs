using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Sessions;

public sealed partial class DocumentSession
{
    private readonly SemanticReview _semantic;
    private readonly Func<ISaveDocument> _openOriginal;

    private void EnsureOriginal() => _semantic.EnsureOriginal(_openOriginal);

    private void RememberSemanticLazy(string lazyId) => _expanded.Add(lazyId);

    private void IndexSemanticField(FieldNode field) => _index.Add(field);

    public SemanticChangeLog GetChanges() => Locked(() =>
    {
        EnsureOriginal();
        return _semantic.Query(Document, IndexSemanticField, RememberSemanticLazy, _revision);
    });

    /// <summary>Restores one existing editable field to its original value; dependent values are recomputed by the plugin.</summary>
    public SessionEditResult RevertChange(string valueId, long expectedRevision) => Locked(() =>
    {
        if (StaleCheck(expectedRevision, [valueId]) is { } stale) return stale;
        EnsureOriginal();
        var log = _semantic.Query(Document, IndexSemanticField, RememberSemanticLazy, _revision);
        if (!log.Changes.Any(c => c.ValueId == valueId && c.CanRevert) || !_semantic.TryOriginal(valueId, out var original))
        {
            return Rejected(valueId, "This change cannot be reverted individually. Use undo for structural or derived changes.");
        }

        return RunGroup(s => s.ApplyEdits([new IncomingEdit(valueId, JsonSerializer.SerializeToElement(original))], expectedRevision));
    });
}
