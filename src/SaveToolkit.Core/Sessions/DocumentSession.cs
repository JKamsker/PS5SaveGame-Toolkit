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
    private ViewIndex _index;
    private readonly EditHistory _history;
    private readonly ReferenceFieldService _referenceFields;
    private readonly ReferenceContext _references;
    private readonly long _maxSnapshotBytes;
    private bool _disposed;
    private bool _idleRetired;
    private bool _unusable;

    public DocumentSession(string id, DiskSaveFile file, IGamePlugin plugin, ISaveDocument document,
        DocumentSessionOptions? options = null, Func<ISaveDocument>? openOriginal = null, ReferenceContext? references = null)
    {
        Id = id;
        File = file;
        Plugin = plugin;
        Document = document;
        _index = new ViewIndex(document);
        var limits = options ?? DocumentSessionOptions.Default;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaxQueryActionRows);
        _maxQueryActionRows = limits.MaxQueryActionRows;
        _maxSnapshotBytes = limits.MaxSnapshotBytes;
        var referenceContext = references ?? ReferenceContext.None;
        _references = referenceContext;
        _history = new EditHistory(limits);
        _referenceFields = new ReferenceFieldService(referenceContext);
        _semantic = new SemanticReview(limits);
        OutputOwnership = new OutputOwnershipBinding(file, document);
        // The original is re-opened with the same pinned reference packs so its field identities match the live document.
        _openOriginal = openOriginal ?? (() => plugin.Open(file, referenceContext));
    }

    public string Id { get; }
    public DiskSaveFile File { get; }
    public IGamePlugin Plugin { get; }
    public string Title => Locked(() => Document.Title);
    public bool CanEdit => Locked(() => !_unusable && Document.CanEdit);

    private ISaveDocument Document { get; }
    private OutputOwnershipBinding OutputOwnership { get; }

    /// <summary>Full save-set paths reserved by this document, including exact declared system files.</summary>
    internal IReadOnlyList<string> OwnedOutputPaths => OutputOwnership.OwnedPaths;
    internal IReadOnlyList<string> DeclaredOutputPaths => OutputOwnership.DeclaredPaths;
    internal bool RequiresCompleteOutputs => OutputOwnership.RequiresCompleteOutputs;
    internal void CheckOutputFingerprints(CancellationToken cancellationToken) => OutputOwnership.Check(cancellationToken);

    public DocumentState State => Locked(StateUnlocked);

    public DocumentState GetState(CancellationToken cancellationToken) => Locked(StateUnlocked, cancellationToken);

    /// <summary>Captures the view and its revision together, without an edit or undo interleaving.</summary>
    public DocumentViewSnapshot CaptureView() => Locked(() =>
        new DocumentViewSnapshot(Document.Title, Document.CanEdit, StateUnlocked(), BuildView()));

    public ViewNode BuildView() => Locked(() => PublishView(staged =>
    {
        var view = Document.BuildView();
        staged.Add(view);
        return staged.ResolveFieldReferences(Document, view);
    }));

    public IReadOnlyList<ViewNode> Expand(string lazyId) => Locked(() =>
    {
        if (!_index.KnowsLazy(lazyId))
        {
            throw _retired.Contains(lazyId) ? new StaleViewException() : new KeyNotFoundException($"Unknown lazy node '{lazyId}'.");
        }

        var resolved = PublishView(staged =>
        {
            var nodes = Document.ExpandNode(lazyId);
            staged.AddAll(nodes);
            return ((SectionNode)staged.ResolveFieldReferences(Document, new SectionNode(string.Empty, nodes))).Children;
        });
        _expanded.Add(lazyId);
        return resolved;
    });

    // A rejected response must publish neither bindings/metadata nor remembered group pages.
    private T PublishView<T>(Func<ViewIndex, T> prepare, CancellationToken cancellationToken = default)
    {
        var staged = _index.Copy(cancellationToken);
        var restoreGroup = (Document as Groups.StagedGroupDocument)?.CancellationRollback();
        try
        {
            var result = prepare(staged);
            cancellationToken.ThrowIfCancellationRequested();
            _index = staged;
            return result;
        }
        catch
        {
            restoreGroup?.Invoke();
            throw;
        }
    }

    /// <param name="incoming">Edits as sent by the client.</param>
    /// <param name="expectedRevision">The <see cref="DocumentState.Revision"/> the client's view was built at, if known.</param>
    public SessionEditResult ApplyEdits(IReadOnlyList<IncomingEdit> incoming, long? expectedRevision = null) =>
        ApplyEdits(incoming, expectedRevision, CancellationToken.None);

    public SessionEditResult ApplyEdits(IReadOnlyList<IncomingEdit> incoming, long? expectedRevision, CancellationToken cancellationToken) => Locked(() => RunPreflight(startApply =>
    {
        if (_unusable) return Rejected(incoming.FirstOrDefault()?.ValueId ?? string.Empty, "The document session is unusable; reopen it before editing or exporting.");
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
        if (ResolveEditMetadata(incoming.Select(edit => edit.ValueId), cancellationToken) is { } metadataError) return metadataError;
        foreach (var edit in incoming)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_index.ResolvedSpec(edit.ValueId) is not { } spec)
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

        _semantic.CaptureTargetedOriginals(_openOriginal, edits.Select(edit => edit.ValueId), cancellationToken);
        // Point of no return: mutation and history/revision publication must finish together.
        startApply();
        var outcome = CallPlugin(() => Document.ApplyEdits(edits), edits.FirstOrDefault()?.ValueId);
        if (!outcome.Succeeded)
        {
            return FromOutcome(outcome);
        }

        var warnings = outcome.Revert is { } revert
            ? Record(new MementoStep(revert))
            : outcome.Changes.Count > 0 ? Record(new ValueStep(outcome.Changes)) : [];
        return AfterChange(FromOutcome(outcome), outcome.StructureChanged || outcome.Revert is not null, warnings);
    }, cancellationToken), cancellationToken);

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
    public IReadOnlyList<FileOutput> Serialize() => Serialize(CancellationToken.None);

    public IReadOnlyList<FileOutput> Serialize(CancellationToken cancellationToken) =>
        CaptureOutputs(cancellationToken).Outputs;

    /// <summary>Captures output bytes and the dirty/revision state that produced them atomically.</summary>
    internal DocumentOutputCapture CaptureOutputs(CancellationToken cancellationToken) =>
        Locked(() =>
        {
            if (_unusable) throw new InvalidOperationException("The document session is unusable; reopen it before exporting.");
            var state = StateUnlocked();
            var outputs = state.IsDirty ? DocumentOutputSnapshot.Capture(Document, cancellationToken) : [];
            return new DocumentOutputCapture(outputs, state.IsDirty, state.Revision);
        }, cancellationToken);

    internal void MarkUnusable()
    {
        _unusable = true;
        _pendingAction = null;
        _history.Clear(irreversible: true);
        _groupBroken = true;
    }

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

    // Workspace idle eviction holds all clean session gates through unregistration and
    // retirement. Never wait for a plugin operation or inspect/serialize plugin state.
    internal bool TryEnterCleanState()
    {
        if (_gate.IsHeldByCurrentThread || !_gate.TryEnter()) return false;
        if (_disposed || !StateUnlocked().IsDirty) return true;
        _gate.Exit();
        return false;
    }
    internal void RetireCleanState() => _idleRetired = true;
    internal void ExitCleanState() => _gate.Exit();

    private EditOutcome Revert(IReadOnlyList<ValueChange> batch, bool useOld)
    {
        try
        {
            var edits = FieldMetadataReplay.Edits(_index, batch, useOld);
            return CallPlugin(() => Document.ApplyEdits(edits), edits.FirstOrDefault()?.ValueId);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return EditOutcome.Rejected(batch.FirstOrDefault()?.ValueId ?? string.Empty, $"Field metadata failed: {ex.Message}");
        }
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
        => Locked(func, CancellationToken.None);

    private T Locked<T>(Func<T> func, CancellationToken cancellationToken)
        => OperationCancellation.Locked(_gate, () =>
        {
            ObjectDisposedException.ThrowIf(_disposed || _idleRetired, this);
            return func();
        }, cancellationToken);
}
