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

    public bool Succeeded => Errors.Count == 0;

    public static EditOutcome Rejected(string valueId, string message) =>
        new() { Errors = [new FieldError(valueId, message)] };
}

public sealed record ActionOutcome
{
    public string? Message { get; init; }

    /// <summary>
    /// Changes made by the action. When <see cref="Undoable"/> is false (e.g. structural changes not
    /// expressible as value changes) the framework clears the undo history.
    /// </summary>
    public IReadOnlyList<ValueChange> Changes { get; init; } = [];

    public bool Undoable { get; init; } = true;

    public bool StructureChanged { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record FileOutput(string RelativePath, ReadOnlyMemory<byte> Content);

public sealed record AssetContent(string ContentType, ReadOnlyMemory<byte> Content);
