using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Sessions;

public sealed record IncomingEdit(string ValueId, JsonElement Value);

public sealed record SessionEditResult
{
    public bool Ok => Errors.Count == 0;
    public IReadOnlyList<FieldError> Errors { get; init; } = [];
    public IReadOnlyList<ValueChange> Changes { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string? Message { get; init; }
    public bool StructureChanged { get; init; }

    /// <summary>
    /// The request used an old document revision or a retired view id.
    /// Nothing changed; the client should refetch the view.
    /// </summary>
    public bool Stale { get; init; }
    public string? Code => Stale ? "stale" : null;

    public required DocumentState State { get; init; }
}

public sealed record DocumentState(bool CanUndo, bool CanRedo, bool IsDirty)
{
    /// <summary>Document revision; increases on every mutation, including undo and redo. Clients send it back with edits and actions.</summary>
    public long Revision { get; init; }
}

/// <summary>
/// One opened document. All plugin calls go through here, serialised by a lock, with incoming values
/// validated against specs the plugin itself published, and one undo/redo history for value edits and
/// structural changes (see <see cref="ISaveDocument.RestoreMemento"/>).
/// </summary>
public sealed partial class DocumentSession : IDisposable
{
    private readonly Lock _gate = new();
    private ViewIndex _index = new();
    private readonly EditHistory _history;
    private readonly ReferenceFieldService _referenceFields;
    private readonly ReferenceContext _references;
    private bool _disposed;

    public DocumentSession(string id, DiskSaveFile file, IGamePlugin plugin, ISaveDocument document,
        DocumentSessionOptions? options = null, Func<ISaveDocument>? openOriginal = null, ReferenceContext? references = null)
    {
        Id = id;
        File = file;
        Plugin = plugin;
        Document = document;
        var limits = options ?? DocumentSessionOptions.Default;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaxQueryActionRows);
        _maxQueryActionRows = limits.MaxQueryActionRows;
        var referenceContext = references ?? ReferenceContext.None;
        _references = referenceContext;
        _history = new EditHistory(limits);
        _referenceFields = new ReferenceFieldService(referenceContext);
        _semantic = new SemanticReview(limits.MaxOriginalBytes);
        // The original is re-opened with the same pinned reference packs so its field identities match the live document.
        _openOriginal = openOriginal ?? (() => plugin.Open(file, referenceContext));
    }

    public string Id { get; }
    public DiskSaveFile File { get; }
    public IGamePlugin Plugin { get; }
    public string Title => Locked(() => Document.Title);
    public bool CanEdit => Locked(() => Document.CanEdit);

    private ISaveDocument Document { get; }

    public DocumentState State => Locked(StateUnlocked);

    /// <summary>Captures the view and its revision together, without an edit or undo interleaving.</summary>
    public DocumentViewSnapshot CaptureView() => Locked(() =>
        new DocumentViewSnapshot(Document.Title, Document.CanEdit, StateUnlocked(), BuildView()));

    public ViewNode BuildView() => Locked(() =>
    {
        var view = Document.BuildView();
        _index.Add(view);
        return view;
    });

    public IReadOnlyList<ViewNode> Expand(string lazyId) => Locked(() =>
    {
        if (!_index.KnowsLazy(lazyId))
        {
            throw _retired.Contains(lazyId) ? new StaleViewException() : new KeyNotFoundException($"Unknown lazy node '{lazyId}'.");
        }

        var nodes = Document.ExpandNode(lazyId);
        _index.AddAll(nodes);
        _expanded.Add(lazyId);
        return nodes;
    });

    /// <param name="incoming">Edits as sent by the client.</param>
    /// <param name="expectedRevision">The <see cref="DocumentState.Revision"/> the client's view was built at, if known.</param>
    public SessionEditResult ApplyEdits(IReadOnlyList<IncomingEdit> incoming, long? expectedRevision = null) => Locked(() =>
    {
        if (!Document.CanEdit)
        {
            return Rejected(incoming.FirstOrDefault()?.ValueId ?? string.Empty, "This document is read-only.");
        }

        if (StaleCheck(expectedRevision, incoming.Select(e => e.ValueId)) is { } stale)
        {
            return stale;
        }

        var edits = new List<ValueEdit>(incoming.Count);
        var errors = new List<FieldError>();
        foreach (var edit in incoming)
        {
            if (_index.Spec(edit.ValueId) is not { } spec)
            {
                errors.Add(new FieldError(edit.ValueId, "Unknown field."));
                continue;
            }

            var coerced = FieldValueCoercer.FromJson(edit.Value, spec, _referenceFields);
            if (coerced.Ok)
            {
                edits.Add(new ValueEdit(edit.ValueId, coerced.Value));
            }
            else
            {
                errors.Add(new FieldError(edit.ValueId, coerced.Error!));
            }
        }

        if (errors.Count > 0)
        {
            return new SessionEditResult { Errors = errors, State = StateUnlocked() };
        }

        EnsureOriginal();
        var outcome = CallPlugin(() => Document.ApplyEdits(edits), edits.FirstOrDefault()?.ValueId);
        if (!outcome.Succeeded)
        {
            return FromOutcome(outcome);
        }

        var warnings = outcome.Revert is { } revert
            ? Record(new MementoStep(revert))
            : outcome.Changes.Count > 0 ? Record(new ValueStep(outcome.Changes)) : [];
        return AfterChange(FromOutcome(outcome), outcome.StructureChanged || outcome.Revert is not null, warnings);
    });

    public SessionEditResult Undo() => Locked(() =>
        _group is not null ? Rejected(string.Empty, "Undo is not available inside a grouped operation.")
        : _history.NextUndo is { } entry ? Step(entry, undo: true)
        : Rejected(string.Empty, "Nothing to undo."));

    public SessionEditResult Redo() => Locked(() =>
        _group is not null ? Rejected(string.Empty, "Redo is not available inside a grouped operation.")
        : _history.NextRedo is { } entry ? Step(entry, undo: false)
        : Rejected(string.Empty, "Nothing to redo."));

    /// <summary>
    /// New file contents; empty when the document equals the original (never edited, or everything undone).
    /// Imported blobs are never modified, so exporting twice yields the same result.
    /// </summary>
    public IReadOnlyList<FileOutput> Serialize() => Locked<IReadOnlyList<FileOutput>>(() => StateUnlocked().IsDirty
        ? Document.Serialize().Select(output => output with { Content = output.Content.ToArray() }).ToArray()
        : []);

    public AssetContent? GetAsset(string assetId) => Locked(() =>
        _index.KnowsAsset(assetId) && Document.GetAsset(assetId) is { } asset
            ? asset with { Content = asset.Content.ToArray() } : null);

    /// <summary>Field-specific candidates, using the same catalog and policy as edits.</summary>
    public ReferenceCandidatePage SearchReferences(string valueId, ReferenceQuery query, long? revision = null, string? actionId = null) =>
        Locked(() => _referenceFields.Search(ReferenceField(valueId, revision, actionId), query, _revision));

    /// <summary>Resolves any exact raw id, including an unknown current value, without changing the document.</summary>
    public ReferenceCandidate FindReference(string valueId, string rawId, long? revision = null, string? actionId = null) =>
        Locked(() => _referenceFields.Find(ReferenceField(valueId, revision, actionId), rawId));

    private ReferenceSpec ReferenceField(string valueId, long? revision, string? actionId)
    {
        if ((revision is { } expected && expected != _revision) || _retired.Contains(actionId ?? valueId))
        {
            throw new StaleViewException();
        }

        var spec = actionId is null ? _index.Spec(valueId) : _index.Action(actionId)?.Parameters.FirstOrDefault(p => p.ValueId == valueId)?.Spec;
        return spec as ReferenceSpec ?? throw new KeyNotFoundException($"Unknown reference field '{valueId}'.");
    }

    public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) => Locked<ReadOnlyMemory<byte>>(() =>
        _index.KnowsBlob(blobId) ? Document.ReadBlob(blobId, offset, count).ToArray() : throw new KeyNotFoundException($"Unknown blob '{blobId}'."));

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Document.Dispose();
        }
    }

    /// <summary>Atomically closes a clean session; an in-flight edit is either completed or rejected by disposal.</summary>
    internal bool TryCloseClean(Action releaseOwnership) => Locked(() =>
    {
        if (StateUnlocked().IsDirty) return false;
        try { Dispose(); }
        finally { releaseOwnership(); }
        return true;
    });

    private EditOutcome Revert(IReadOnlyList<ValueChange> batch, bool useOld)
    {
        // Derived values are usually read-only; re-applying the primary edits recomputes them.
        // Values the plugin reported but never showed (e.g. bulk actions over unexpanded lazy pages)
        // are replayed too: they came from the plugin itself, so no spec check is needed.
        var edits = (useOld ? batch.Reverse() : batch)
            .Where(c => _index.Spec(c.ValueId) is not { ReadOnly: true })
            .Select(c => new ValueEdit(c.ValueId, useOld ? c.OldValue : c.NewValue))
            .ToList();
        return CallPlugin(() => Document.ApplyEdits(edits), edits.FirstOrDefault()?.ValueId);
    }

    private static EditOutcome CallPlugin(Func<EditOutcome> call, string? valueId)
    {
        try
        {
            return call();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return EditOutcome.Rejected(valueId ?? string.Empty, $"Plugin error: {ex.Message}");
        }
    }

    private SessionEditResult FromOutcome(EditOutcome outcome) => new()
    {
        Errors = outcome.Errors,
        Changes = outcome.Changes,
        Warnings = outcome.Warnings,
        StructureChanged = outcome.StructureChanged,
        State = StateUnlocked(),
    };

    private SessionEditResult Rejected(string valueId, string message) =>
        new() { Errors = [new FieldError(valueId, message)], State = StateUnlocked() };

    private DocumentState StateUnlocked() =>
        new(_history.CanUndo, _history.CanRedo, _history.CanUndo || _history.IrreversiblyChanged) { Revision = _revision };

    private T Locked<T>(Func<T> func)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return func();
        }
    }
}
