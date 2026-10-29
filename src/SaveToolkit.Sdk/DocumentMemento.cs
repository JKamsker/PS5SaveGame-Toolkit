namespace SaveToolkit.Sdk;

/// <summary>
/// Opaque token that takes a document back across one structural change (records added, removed,
/// reordered or replaced). The framework stores it in the undo history and hands it back to
/// <see cref="ISaveDocument.RestoreMemento"/>; it never looks inside.
/// </summary>
/// <remarks>
/// Two kinds of token fit this contract, and the framework treats them the same way:
/// <list type="bullet">
/// <item>a <b>snapshot</b> of the whole editable state (<see cref="Snapshot{T}"/>), the simple default;</item>
/// <item>an <b>inverse operation</b> ("remove record r7"), an optional optimisation for large documents.</item>
/// </list>
/// Restoring a token must return the token that reverses that restore. That is how redo works.
/// </remarks>
public abstract class DocumentMemento
{
    /// <summary>Approximate memory the token holds. The framework bounds history by the sum of these sizes.</summary>
    public abstract long SizeBytes { get; }

    /// <summary>Wraps a plugin-owned state object. <paramref name="state"/> must not be mutated afterwards.</summary>
    public static SnapshotMemento<T> Snapshot<T>(T state, long sizeBytes) where T : notnull => new(state, sizeBytes);
}

/// <summary>Whole-state snapshot produced by <see cref="DocumentMemento.Snapshot{T}"/>.</summary>
public sealed class SnapshotMemento<T> : DocumentMemento where T : notnull
{
    internal SnapshotMemento(T state, long sizeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);
        State = state;
        SizeBytes = sizeBytes;
    }

    /// <summary>Plugin-owned immutable state; copy it when restoring into a mutable document.</summary>
    public T State { get; }

    /// <inheritdoc />
    public override long SizeBytes { get; }
}
