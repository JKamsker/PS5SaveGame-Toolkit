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
        try
        {
            foreach (var edit in edits)
            {
                var binding = _bindings[edit.ValueId];
                var old = binding.Get();
                binding.Set!(edit.Value);
                applied.Add(new ValueChange(edit.ValueId, old, binding.Get()));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException or SaveFormatException)
        {
            Rollback(applied);
            return EditOutcome.Rejected(applied.Count < edits.Count ? edits[applied.Count].ValueId : edits[^1].ValueId, ex.Message);
        }

        var derived = DerivedChanges(applied);
        return new EditOutcome { Changes = [.. applied, .. derived] };
    }

    /// <summary>Override to report values that changed as a consequence of an edit (checksums, totals...).</summary>
    protected virtual IEnumerable<ValueChange> DerivedChanges(IReadOnlyList<ValueChange> applied) => [];

    /// <summary>Applies changes through the bindings without validation (used by actions).</summary>
    protected IReadOnlyList<ValueChange> SetValues(IEnumerable<ValueEdit> edits)
    {
        var applied = new List<ValueChange>();
        foreach (var edit in edits)
        {
            var binding = Lookup(edit.ValueId);
            var old = binding.Get();
            binding.Set!(edit.Value);
            applied.Add(new ValueChange(edit.ValueId, old, binding.Get()));
        }

        return applied;
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
