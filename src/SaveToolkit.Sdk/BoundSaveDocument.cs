using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Sdk;

/// <summary>
/// Convenience base for documents whose values map to getter/setter pairs on an in-memory model.
/// Call <see cref="Field"/> while building views; it registers the binding and returns the node.
/// Batches are applied atomically: on a validation failure or exception every applied edit is rolled back.
/// </summary>
public abstract class BoundSaveDocument : ISaveDocument
{
    private readonly Dictionary<string, Binding> _bindings = new(StringComparer.Ordinal);

    public abstract string Title { get; }

    public virtual bool CanEdit => true;

    public abstract ViewNode BuildView();

    public virtual IReadOnlyList<ViewNode> ExpandNode(string lazyId) =>
        throw new KeyNotFoundException($"Unknown lazy node '{lazyId}'.");

    public abstract IReadOnlyList<FileOutput> Serialize();

    public virtual ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
        throw new KeyNotFoundException($"Unknown action '{actionId}'.");

    public virtual AssetContent? GetAsset(string assetId) => null;

    public virtual ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) =>
        throw new KeyNotFoundException($"Unknown blob '{blobId}'.");

    /// <summary>
    /// Registers a bound value and returns its view node. Without <paramref name="set"/> the field is read-only.
    /// <paramref name="validate"/> returns an error message or null; it runs after the framework's spec checks.
    /// </summary>
    protected FieldNode Field(
        string valueId,
        string label,
        FieldSpec spec,
        Func<FieldValue> get,
        Action<FieldValue>? set = null,
        Func<FieldValue, string?>? validate = null)
    {
        var effectiveSpec = set is null ? spec with { ReadOnly = true } : spec;
        _bindings[valueId] = new Binding(effectiveSpec, get, set, validate);
        return new FieldNode(valueId, label, effectiveSpec, get());
    }

    public FieldValue GetValue(string valueId) => Lookup(valueId).Get();

    public virtual EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var errors = new List<FieldError>();
        foreach (var edit in edits)
        {
            if (!_bindings.TryGetValue(edit.ValueId, out var binding))
            {
                errors.Add(new FieldError(edit.ValueId, "Unknown field."));
            }
            else if (binding.Set is null)
            {
                errors.Add(new FieldError(edit.ValueId, "Field is read-only."));
            }
            else if (binding.Validate?.Invoke(edit.Value) is { } message)
            {
                errors.Add(new FieldError(edit.ValueId, message));
            }
        }

        if (errors.Count > 0)
        {
            return new EditOutcome { Errors = errors };
        }

        var applied = new List<ValueChange>(edits.Count);
        var currentId = string.Empty;
        try
        {
            foreach (var edit in edits)
            {
                currentId = edit.ValueId;
                ApplyValue(edit, applied);
            }

            // Materialize lazy derived changes inside the transaction as well.
            return new EditOutcome { Changes = [.. applied, .. DerivedChanges(applied)] };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Rollback(applied);
            return EditOutcome.Rejected(currentId, ex.Message);
        }
    }

    /// <summary>Override to report values that changed as a consequence of an edit (checksums, totals...).</summary>
    protected virtual IEnumerable<ValueChange> DerivedChanges(IReadOnlyList<ValueChange> applied) => [];

    /// <summary>Atomically applies changes through the bindings without validation (used by actions).</summary>
    protected IReadOnlyList<ValueChange> SetValues(IEnumerable<ValueEdit> edits)
    {
        var applied = new List<ValueChange>();
        try
        {
            foreach (var edit in edits)
            {
                ApplyValue(edit, applied);
            }

            return applied;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Rollback(applied);
            throw;
        }
    }

    private void ApplyValue(ValueEdit edit, List<ValueChange> applied)
    {
        var binding = Lookup(edit.ValueId);
        var old = binding.Get();
        // Include the attempted setter: it or its readback can throw after mutating.
        applied.Add(new ValueChange(edit.ValueId, old, old));
        binding.Set!(edit.Value);
        applied[^1] = new ValueChange(edit.ValueId, old, binding.Get());
    }

    /// <summary>
    /// Runs a structural change (add/remove/reorder/replace records) as one undoable, atomic step.
    /// Requires <see cref="CaptureSnapshot"/> and <see cref="LoadSnapshot"/>. If <paramref name="mutate"/>
    /// throws, the snapshot is loaded back and the exception propagates, so a failed precondition
    /// changes nothing. Afterwards (also after a failure) all bindings are dropped; the framework
    /// rebuilds the view, which registers the live ones again.
    /// </summary>
    protected ActionOutcome Structural(Action mutate, string? message = null)
    {
        var before = CaptureSnapshot()
            ?? throw new InvalidOperationException("Override CaptureSnapshot and LoadSnapshot to use structural actions.");
        try
        {
            mutate();
        }
        catch
        {
            LoadSnapshot(before);
            throw;
        }
        finally
        {
            // Loading a snapshot may replace model objects, so every binding may point at dead state.
            _bindings.Clear();
        }

        return new ActionOutcome { StructureChanged = true, Revert = before, Message = message };
    }

    /// <summary>
    /// Snapshot of every piece of editable state, including record identities and id counters, so that
    /// <see cref="LoadSnapshot"/> recreates the same bytes and ids. Return null (the default) when the
    /// document has no structural actions.
    /// </summary>
    protected virtual DocumentMemento? CaptureSnapshot() => null;

    /// <summary>Replaces the editable state with a snapshot from <see cref="CaptureSnapshot"/>.</summary>
    protected virtual void LoadSnapshot(DocumentMemento snapshot) =>
        throw new NotSupportedException("This document does not support structural undo.");

    /// <summary>
    /// Snapshot-based implementation: captures the current state (returned for redo), loads
    /// <paramref name="memento"/> and drops bindings. Override for inverse-operation tokens.
    /// </summary>
    public virtual DocumentMemento RestoreMemento(DocumentMemento memento)
    {
        var current = CaptureSnapshot()
            ?? throw new NotSupportedException("This document does not support structural undo.");
        try
        {
            LoadSnapshot(memento);
        }
        catch
        {
            LoadSnapshot(current);
            throw;
        }
        finally
        {
            _bindings.Clear();
        }

        return current;
    }

    private void Rollback(List<ValueChange> applied)
    {
        for (var i = applied.Count - 1; i >= 0; i--)
        {
            _bindings[applied[i].ValueId].Set!(applied[i].OldValue);
        }
    }

    private Binding Lookup(string valueId) =>
        _bindings.TryGetValue(valueId, out var b) ? b : throw new KeyNotFoundException($"Unknown field '{valueId}'.");

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    private sealed record Binding(FieldSpec Spec, Func<FieldValue> Get, Action<FieldValue>? Set, Func<FieldValue, string?>? Validate);
}
