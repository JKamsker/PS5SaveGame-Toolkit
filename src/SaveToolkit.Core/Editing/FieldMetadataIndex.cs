using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Editing;

/// <summary>Targeted, bounded discovery against the current document, never a rendered page.</summary>
internal sealed class FieldMetadataIndex(ISaveDocument? document, long? maxMetadataBytes = null)
{
    internal const int BatchSize = 200;
    private readonly Dictionary<string, FieldMetadata> _fields = new(StringComparer.Ordinal);
    private readonly long? _maxMetadataBytes = maxMetadataBytes;
    public bool Enabled => document is IFieldMetadataDocument
        && (document is not SaveToolkit.Core.Sessions.Groups.StagedGroupDocument staged || staged.MetadataEnabled);
    public IEnumerable<string> Ids => _fields.Keys;
    public void Clear() => _fields.Clear();
    internal FieldMetadataIndex Copy(CancellationToken cancellationToken)
    {
        var copy = new FieldMetadataIndex(document, _maxMetadataBytes);
        copy._fields.EnsureCapacity(_fields.Count);
        foreach (var entry in _fields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            copy._fields.Add(entry.Key, entry.Value);
        }
        return copy;
    }
    public FieldMetadata? Cached(string id) => _fields.GetValueOrDefault(id);

    public FieldMetadata? Resolve(string id)
    {
        ResolveAll([id]);
        return _fields.GetValueOrDefault(id);
    }

    /// <summary>Returns the explicitly published canonical primary for an ID, if one is known.</summary>
    public FieldMetadata? Canonical(string id)
    {
        ResolveAll([id]);
        var field = _fields.GetValueOrDefault(id);
        var canonicalId = field is null || string.IsNullOrEmpty(field.CanonicalValueId)
            ? field?.ValueId : field.CanonicalValueId;
        if (field is null || canonicalId == field.ValueId) return field;
        ResolveAll([canonicalId!]);
        return _fields.GetValueOrDefault(canonicalId!);
    }

    public void ResolveAll(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var working = new Dictionary<string, FieldMetadata>(_fields, StringComparer.Ordinal);
        ResolveAll(ids, working, cancellationToken);
        _fields.Clear();
        foreach (var (id, field) in working) _fields.Add(id, field);
    }

    private void ResolveAll(IEnumerable<string> ids, Dictionary<string, FieldMetadata> cache,
        CancellationToken cancellationToken)
    {
        if (document is not IFieldMetadataDocument source) return;
        var pending = new Queue<string>(ids.Distinct(StringComparer.Ordinal));
        var scheduled = pending.ToHashSet(StringComparer.Ordinal);
        var fetched = new HashSet<string>(StringComparer.Ordinal);
        var touched = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = pending.Take(BatchSize).ToArray();
            foreach (var id in batch) _ = pending.Dequeue();
            var requested = batch.ToHashSet(StringComparer.Ordinal);
            var resolved = source.ResolveFieldMetadata(Array.AsReadOnly(batch));
            if (resolved.Count > batch.Length) throw new InvalidOperationException("Too many field descriptors returned.");
            var next = new Dictionary<string, FieldMetadata>(StringComparer.Ordinal);
            foreach (var field in resolved)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalized = Normalize(field);
                if (string.IsNullOrEmpty(normalized.ValueId) || !requested.Contains(normalized.ValueId)
                    || string.IsNullOrEmpty(normalized.Label) || normalized.Spec is null
                    || !Enum.IsDefined(normalized.BindingKind)
                    || (normalized.Presentation?.Confidence is { } confidence && !Enum.IsDefined(confidence))
                    || (normalized.BindingKind == FieldBindingKind.Derived && !normalized.Spec.ReadOnly)
                    || !next.TryAdd(normalized.ValueId, normalized))
                    throw new InvalidOperationException("Invalid or duplicate field metadata.");
                _ = document.GetValue(normalized.ValueId); // A descriptor promises a live binding.
            }
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var id in batch.Where(id => !next.ContainsKey(id))) cache.Remove(id);
            foreach (var (id, field) in next)
            {
                if (!cache.TryGetValue(id, out var previous) || !Agree(previous, field))
                {
                    if (_maxMetadataBytes is { } remaining
                        && !SemanticMetadataBudget.TryEstimate(field, remaining, out _))
                        throw new InvalidOperationException("Field metadata exceeds the bounded review budget.");
                    cache[id] = Snapshot(field);
                }
                touched.Add(id);
                fetched.Add(id);
            }

            foreach (var field in next.Values)
            {
                var canonicalId = Normalize(field).CanonicalValueId;
                if (canonicalId != field.ValueId && !fetched.Contains(canonicalId)
                    && scheduled.Add(canonicalId))
                {
                    if (scheduled.Count > FieldMetadataLimits.MaxClosureIds)
                        throw new InvalidOperationException("Canonical metadata closure exceeded its bounded limit.");
                    pending.Enqueue(canonicalId);
                }
            }

            // A target that has arrived can reject an alias-of-derived relationship immediately;
            // no recursive chain traversal is needed. Final agreement waits until all batches finish.
            foreach (var field in cache.Values.Where(field => touched.Contains(field.ValueId)))
                if (HasAvailableTarget(field, fetched)) ValidateCanonical(field, cache);
        }
        foreach (var field in cache.Values.Where(field => touched.Contains(field.ValueId)
            || touched.Contains(Normalize(field).CanonicalValueId)))
            ValidateCanonical(field, cache);
    }

    private static bool HasAvailableTarget(FieldMetadata field, IReadOnlySet<string> fetched) =>
        Normalize(field).CanonicalValueId == field.ValueId || fetched.Contains(Normalize(field).CanonicalValueId);

    private static void ValidateCanonical(FieldMetadata field, IReadOnlyDictionary<string, FieldMetadata> cache)
    {
        var canonicalId = Normalize(field).CanonicalValueId;
        if (canonicalId == field.ValueId)
            return;
        if (field.BindingKind != FieldBindingKind.Derived || !field.Spec.ReadOnly
            || !cache.TryGetValue(canonicalId, out var canonical)
            || canonical.BindingKind != FieldBindingKind.Primary
            || canonical.Spec.ReadOnly
            || Normalize(canonical).CanonicalValueId != canonical.ValueId
            || !FieldSpecMetadata.Agree(field.Spec, canonical.Spec with { ReadOnly = true }))
            throw new InvalidOperationException($"Invalid canonical alias '{field.ValueId}'.");
    }

    private static FieldMetadata Normalize(FieldMetadata field) =>
        string.IsNullOrEmpty(field.CanonicalValueId) ? field with { CanonicalValueId = field.ValueId } : field;

    // Copy nested option/union lists too: plugin-owned mutable collections must not alter a preview.
    private static FieldMetadata Snapshot(FieldMetadata field) => field with
    {
        Spec = FieldSpecMetadata.Snapshot(field.Spec),
        Path = field.Path is { } path ? Array.AsReadOnly(path.ToArray()) : null,
    };

    internal static bool Agree(FieldMetadata first, FieldMetadata second) =>
        FieldSpecMetadata.Agree(first.Spec, second.Spec) && WireAgree(first, second);

    internal static bool Agree(IReadOnlyList<FieldMetadata> first, IReadOnlyList<FieldMetadata> second) =>
        first.Count == second.Count && first.Zip(second).All(pair => Agree(pair.First, pair.Second));

    private static bool WireAgree<T>(T first, T second) =>
        JsonSerializer.SerializeToUtf8Bytes(first).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(second));
}
