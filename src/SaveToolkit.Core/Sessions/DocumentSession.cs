using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
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
    public required DocumentState State { get; init; }
}

public sealed record DocumentState(bool CanUndo, bool CanRedo, bool IsDirty);

/// <summary>
/// One opened document. All plugin calls go through here, serialised by a lock, with incoming values
/// validated against specs the plugin itself published and a value-level undo/redo history.
/// </summary>
public sealed class DocumentSession : IDisposable
{
    private const int MaxHistory = 200;

    private readonly Lock _gate = new();
    private readonly ViewIndex _index = new();
    private readonly LinkedList<IReadOnlyList<ValueChange>> _undo = new();
    private readonly Stack<IReadOnlyList<ValueChange>> _redo = new();
    private bool _irreversiblyChanged;
    private bool _disposed;

    public DocumentSession(string id, DiskSaveFile file, IGamePlugin plugin, ISaveDocument document)
    {
        Id = id;
        File = file;
        Plugin = plugin;
        Document = document;
    }

    public string Id { get; }
    public DiskSaveFile File { get; }
    public IGamePlugin Plugin { get; }
    public string Title => Locked(() => Document.Title);
    public bool CanEdit => Locked(() => Document.CanEdit);

    private ISaveDocument Document { get; }

    public DocumentState State => Locked(StateUnlocked);

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
            throw new KeyNotFoundException($"Unknown lazy node '{lazyId}'.");
        }

        var nodes = Document.ExpandNode(lazyId);
        _index.AddAll(nodes);
        return nodes;
    });

    public SessionEditResult ApplyEdits(IReadOnlyList<IncomingEdit> incoming) => Locked(() =>
    {
        if (!Document.CanEdit)
        {
            return Rejected(incoming.FirstOrDefault()?.ValueId ?? string.Empty, "This document is read-only.");
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

            var coerced = FieldValueCoercer.FromJson(edit.Value, spec);
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

        var outcome = CallPlugin(() => Document.ApplyEdits(edits), edits.FirstOrDefault()?.ValueId);
        if (outcome.Succeeded && outcome.Changes.Count > 0)
        {
            Record(outcome.Changes);
        }

        return FromOutcome(outcome);
    });

    public SessionEditResult InvokeAction(string actionId, IReadOnlyDictionary<string, JsonElement> parameters) => Locked(() =>
    {
        if (_index.Action(actionId) is not { } action)
        {
            return Rejected(actionId, "Unknown action.");
        }

        if (!Document.CanEdit)
        {
            return Rejected(actionId, "This document is read-only.");
        }

        var values = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        var errors = new List<FieldError>();
        foreach (var p in action.Parameters)
        {
            var coerced = parameters.TryGetValue(p.ValueId, out var json)
                ? FieldValueCoercer.FromJson(json, p.Spec)
                : FieldValueCoercer.Normalize(p.Value, p.Spec, allowReadOnly: true);
            if (coerced.Ok)
            {
                values[p.ValueId] = coerced.Value;
            }
            else
            {
                errors.Add(new FieldError(p.ValueId, coerced.Error!));
            }
        }

        if (errors.Count > 0)
        {
            return new SessionEditResult { Errors = errors, State = StateUnlocked() };
        }

        ActionOutcome outcome;
        try
        {
            outcome = Document.InvokeAction(actionId, values);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Rejected(actionId, $"Action failed: {ex.Message}");
        }

        if (outcome.Undoable && outcome.Changes.Count > 0)
        {
            Record(outcome.Changes);
        }
        else if (!outcome.Undoable)
        {
            _undo.Clear();
            _redo.Clear();
            _irreversiblyChanged = true;
        }

        return new SessionEditResult
        {
            Changes = outcome.Changes,
            Warnings = outcome.Warnings,
            Message = outcome.Message,
            StructureChanged = outcome.StructureChanged || !outcome.Undoable,
            State = StateUnlocked(),
        };
    });

    public SessionEditResult Undo() => Locked(() =>
    {
        if (_undo.Last is not { } last)
        {
            return Rejected(string.Empty, "Nothing to undo.");
        }

        var outcome = Revert(last.Value, useOld: true);
        if (outcome.Succeeded)
        {
            _undo.RemoveLast();
            _redo.Push(last.Value);
        }

        return FromOutcome(outcome);
    });

    public SessionEditResult Redo() => Locked(() =>
    {
        if (!_redo.TryPeek(out var batch))
        {
            return Rejected(string.Empty, "Nothing to redo.");
        }

        var outcome = Revert(batch, useOld: false);
        if (outcome.Succeeded)
        {
            _redo.Pop();
            _undo.AddLast(batch);
        }

        return FromOutcome(outcome);
    });

    /// <summary>
    /// New file contents; empty when the document equals the original (never edited, or everything undone).
    /// Imported blobs are never modified, so exporting twice yields the same result.
    /// </summary>
    public IReadOnlyList<FileOutput> Serialize() => Locked(() => StateUnlocked().IsDirty ? Document.Serialize() : []);

    public AssetContent? GetAsset(string assetId) => Locked(() => _index.KnowsAsset(assetId) ? Document.GetAsset(assetId) : null);

    public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) => Locked(() =>
        _index.KnowsBlob(blobId) ? Document.ReadBlob(blobId, offset, count) : throw new KeyNotFoundException($"Unknown blob '{blobId}'."));

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

    private EditOutcome Revert(IReadOnlyList<ValueChange> batch, bool useOld)
    {
        // Derived values are usually read-only; re-applying the primary edits recomputes them.
        var edits = (useOld ? batch.Reverse() : batch)
            .Where(c => _index.Spec(c.ValueId) is { ReadOnly: false })
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

    private void Record(IReadOnlyList<ValueChange> changes)
    {
        _undo.AddLast(changes);
        if (_undo.Count > MaxHistory)
        {
            _undo.RemoveFirst();
            _irreversiblyChanged = true;
        }

        _redo.Clear();
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

    private DocumentState StateUnlocked() => new(_undo.Count > 0, _redo.Count > 0, _undo.Count > 0 || _irreversiblyChanged);

    private T Locked<T>(Func<T> func)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return func();
        }
    }
}
