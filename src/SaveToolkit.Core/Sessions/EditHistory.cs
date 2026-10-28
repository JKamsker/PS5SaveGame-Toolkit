using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>Limits for one document's undo/redo history.</summary>
public sealed record DocumentSessionOptions
{
    public static DocumentSessionOptions Default { get; } = new();

    /// <summary>Maximum number of undo entries; the oldest are dropped beyond this.</summary>
    public int MaxHistory { get; init; } = 200;

    /// <summary>Memory budget for semantic original values; incomplete coverage is explicitly reported.</summary>
    public long MaxOriginalBytes { get; init; } = 16L * 1024 * 1024;

    /// <summary>
    /// Upper bound for the memory held by structural undo tokens (<see cref="DocumentMemento.SizeBytes"/>)
    /// across undo and redo. The oldest history is evicted first; a single change larger than this
    /// is applied without undo and reported with a warning.
    /// </summary>
    public long MaxSnapshotBytes { get; init; } = 256L * 1024 * 1024;
}

/// <summary>One reversible piece of an undo entry.</summary>
internal abstract record HistoryStep
{
    public abstract long SizeBytes { get; }
}

/// <summary>Scalar changes, replayed through <see cref="ISaveDocument.ApplyEdits"/>.</summary>
internal sealed record ValueStep(IReadOnlyList<ValueChange> Changes) : HistoryStep
{
    public override long SizeBytes => 0;
}

/// <summary>Structural change, reverted through <see cref="ISaveDocument.RestoreMemento"/>.</summary>
internal sealed record MementoStep(DocumentMemento Memento) : HistoryStep
{
    public override long SizeBytes => Memento.SizeBytes;
}

/// <summary>
/// One user-visible undo unit. Usually a single step; grouped operations (see
/// <see cref="DocumentSession.RunGroup"/>) hold several, undone in reverse order.
/// </summary>
internal sealed record HistoryEntry(IReadOnlyList<HistoryStep> Steps)
{
    /// <summary>Fixed at creation so the history's byte accounting cannot drift.</summary>
    public long SizeBytes { get; } = Steps.Sum(s => s.SizeBytes);
}

/// <summary>Undo and redo stacks with count and memory limits. Not thread-safe; the session serialises access.</summary>
internal sealed class EditHistory(DocumentSessionOptions options)
{
    private readonly LinkedList<HistoryEntry> _undo = new();
    private readonly LinkedList<HistoryEntry> _redo = new();
    private long _bytes;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>True once a change was made that history can no longer take back.</summary>
    public bool IrreversiblyChanged { get; private set; }

    public HistoryEntry? NextUndo => _undo.Last?.Value;

    public HistoryEntry? NextRedo => _redo.Last?.Value;

    public long Bytes => _bytes;

    /// <summary>Records a new entry, clearing redo. Returns user-facing notices about evicted history.</summary>
    public IReadOnlyList<string> Record(HistoryEntry entry)
    {
        Clear(_redo);
        if (entry.SizeBytes > options.MaxSnapshotBytes)
        {
            Clear(irreversible: true);
            return [$"This change is too large to undo ({Mib(entry.SizeBytes)} MiB, limit {Mib(options.MaxSnapshotBytes)} MiB). Earlier history was cleared."];
        }

        Push(_undo, entry);
        return Enforce();
    }

    /// <summary>Moves the newest undo entry to redo, as <paramref name="inverse"/> (tokens swapped by the restore).</summary>
    public IReadOnlyList<string> CompleteUndo(HistoryEntry inverse)
    {
        Pop(_undo);
        Push(_redo, inverse);
        return Enforce();
    }

    /// <summary>Moves the newest redo entry back to undo, as <paramref name="inverse"/>.</summary>
    public IReadOnlyList<string> CompleteRedo(HistoryEntry inverse)
    {
        Pop(_redo);
        Push(_undo, inverse);
        return Enforce();
    }

    /// <summary>Drops all history, e.g. after a non-undoable action.</summary>
    public void Clear(bool irreversible)
    {
        Clear(_undo);
        Clear(_redo);
        IrreversiblyChanged |= irreversible;
    }

    private IReadOnlyList<string> Enforce()
    {
        while (_undo.Count > options.MaxHistory)
        {
            RemoveFirst(_undo);
            IrreversiblyChanged = true;
        }

        if (_bytes <= options.MaxSnapshotBytes)
        {
            return [];
        }

        // Oldest undo first (furthest from the current state), then the deepest redo entries.
        while (_bytes > options.MaxSnapshotBytes && _undo.Count > 0)
        {
            RemoveFirst(_undo);
            IrreversiblyChanged = true;
        }

        while (_bytes > options.MaxSnapshotBytes && _redo.Count > 0)
        {
            RemoveFirst(_redo);
        }

        return [$"Undo history was trimmed to stay within the {Mib(options.MaxSnapshotBytes)} MiB limit; the oldest steps can no longer be undone."];
    }

    private void Push(LinkedList<HistoryEntry> list, HistoryEntry entry)
    {
        list.AddLast(entry);
        _bytes += entry.SizeBytes;
    }

    private void Pop(LinkedList<HistoryEntry> list)
    {
        _bytes -= list.Last!.Value.SizeBytes;
        list.RemoveLast();
    }

    private void RemoveFirst(LinkedList<HistoryEntry> list)
    {
        _bytes -= list.First!.Value.SizeBytes;
        list.RemoveFirst();
    }

    private void Clear(LinkedList<HistoryEntry> list)
    {
        foreach (var entry in list)
        {
            _bytes -= entry.SizeBytes;
        }

        list.Clear();
    }

    private static string Mib(long bytes) => (bytes / (1024.0 * 1024)).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
