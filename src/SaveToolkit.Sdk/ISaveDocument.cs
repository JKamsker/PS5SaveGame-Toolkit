using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Sdk;

/// <summary>
/// An opened save file. Created by <see cref="IGamePlugin.Open"/> and owned by the framework, which
/// serialises all calls (no concurrent access) and disposes it when the session ends.
/// </summary>
public interface ISaveDocument : IDisposable
{
    /// <summary>Short human title, e.g. "Slot 3 · 25.7 h".</summary>
    string Title { get; }

    /// <summary>False for read-only/analysis plugins; the browser then disables editing and export.</summary>
    bool CanEdit { get; }

    /// <summary>Root of the view. Called again after any outcome with <c>StructureChanged</c>.</summary>
    ViewNode BuildView();

    /// <summary>Children of a lazy <see cref="SectionNode"/>.</summary>
    IReadOnlyList<ViewNode> ExpandNode(string lazyId) =>
        throw new KeyNotFoundException($"Unknown lazy node '{lazyId}'.");

    /// <summary>Current value of a field previously exposed through a <see cref="FieldNode"/>.</summary>
    FieldValue GetValue(string valueId);

    /// <summary>
    /// Applies a batch atomically: if any edit is rejected, nothing may change and the outcome lists errors.
    /// Values arrive already type-checked and range-checked against the field spec.
    /// </summary>
    EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits);

    ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
        throw new KeyNotFoundException($"Unknown action '{actionId}'.");

    /// <summary>
    /// Complete new contents of every file this document rewrites (usually just its own file).
    /// Paths are <see cref="Saves.ISaveFile.RelativePath"/> values of files in the same set.
    /// </summary>
    IReadOnlyList<FileOutput> Serialize();

    AssetContent? GetAsset(string assetId) => null;

    /// <summary>Bytes for a <see cref="HexNode"/>.</summary>
    ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) =>
        throw new KeyNotFoundException($"Unknown blob '{blobId}'.");

    /// <summary>
    /// Undoes or redoes a structural change. <paramref name="memento"/> is a <c>Revert</c> token this document
    /// returned earlier (in <see cref="ActionOutcome.Revert"/> or <see cref="EditOutcome.Revert"/>) or a token
    /// returned by a previous call to this method. Returns the token that reverses this restore.
    /// The restored state must serialise to the same bytes and expose the same value, row, action and lazy
    /// ids as when the token was taken. The framework rebuilds the view afterwards.
    /// Throw (and leave the document unchanged) if the token cannot be applied.
    /// </summary>
    DocumentMemento RestoreMemento(DocumentMemento memento) =>
        throw new NotSupportedException("This document does not support structural undo.");
}

public sealed record ValueEdit(string ValueId, FieldValue Value);

/// <summary>A value that changed; <see cref="OldValue"/> enables undo.</summary>
public sealed record ValueChange(string ValueId, FieldValue OldValue, FieldValue NewValue);

public sealed record FieldError(string ValueId, string Message);

public sealed record EditOutcome
{
    /// <summary>All values that changed, including derived ones (checksums, totals, ...).</summary>
    public IReadOnlyList<ValueChange> Changes { get; init; } = [];

    /// <summary>Non-empty means the batch was rejected and nothing changed.</summary>
    public IReadOnlyList<FieldError> Errors { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Set when rows/sections appeared or vanished, so the view must be rebuilt.</summary>
    public bool StructureChanged { get; init; }

    /// <summary>
    /// Optional token that reverts this batch through <see cref="ISaveDocument.RestoreMemento"/>. When set,
    /// undo restores it instead of replaying <see cref="Changes"/>. Use it when an edit changes structure
    /// (e.g. a type change that replaces a subtree).
    /// </summary>
    public DocumentMemento? Revert { get; init; }

    public bool Succeeded => Errors.Count == 0;

    public static EditOutcome Rejected(string valueId, string message) =>
        new() { Errors = [new FieldError(valueId, message)] };
}

public sealed record ActionOutcome
{
    public string? Message { get; init; }

    /// <summary>
    /// Changes made by the action. When <see cref="Undoable"/> is false (e.g. structural changes not
    /// expressible as value changes, without a <see cref="Revert"/> token) the framework clears the undo history.
    /// </summary>
    public IReadOnlyList<ValueChange> Changes { get; init; } = [];

    public bool Undoable { get; init; } = true;

    public bool StructureChanged { get; init; }

    /// <summary>
    /// Token that takes the document back to the state before this action, through
    /// <see cref="ISaveDocument.RestoreMemento"/>. Set it for structural actions (add/remove/reorder/replace)
    /// to make them undoable; undo then restores it instead of replaying <see cref="Changes"/>.
    /// Ignored when <see cref="Undoable"/> is false.
    /// </summary>
    public DocumentMemento? Revert { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record FileOutput(string RelativePath, ReadOnlyMemory<byte> Content);

public sealed record AssetContent(string ContentType, ReadOnlyMemory<byte> Content);
