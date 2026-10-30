using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Editing;

/// <summary>Targeted, bounded discovery against the current document, never a rendered page.</summary>
internal sealed class FieldMetadataIndex(ISaveDocument? document, long? maxMetadataBytes = null,
    Action<IReadOnlyDictionary<string, FieldMetadata>>? admitSemantic = null, Action? publishSemantic = null)
{
    internal const int BatchSize = 200;
    private readonly Dictionary<string, FieldMetadata> _fields = new(StringComparer.Ordinal);
    private long? _maxMetadataBytes = maxMetadataBytes;
    public bool Enabled => document is IFieldMetadataDocument
        && (document is not SaveToolkit.Core.Sessions.Groups.StagedGroupDocument staged || staged.MetadataEnabled);
    public IEnumerable<string> Ids => _fields.Keys;
    public void Clear() => _fields.Clear();
    internal void SetBudget(long? bytes) => _maxMetadataBytes = bytes;
    internal FieldMetadataIndex Copy(CancellationToken cancellationToken)
    {
        var copy = new FieldMetadataIndex(document, _maxMetadataBytes, admitSemantic, publishSemantic);
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
        => ResolveAll(ids, FieldMetadataLimits.MaxClosureIds, cancellationToken);

    internal void ResolveAll(IEnumerable<string> ids, int maxInitialIds, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxInitialIds);
        var initial = MaterializeInitialIds(ids, maxInitialIds);
        var working = new Dictionary<string, FieldMetadata>(_fields, StringComparer.Ordinal);
        var retainedBytes = _maxMetadataBytes is null ? 0 : MetadataBytes(working.Values);
        ResolveAll(initial, working, ref retainedBytes, maxInitialIds, cancellationToken);
        _fields.Clear();
        foreach (var (id, field) in working) _fields.Add(id, field);
    }

    private void ResolveAll(IReadOnlyList<string> ids, Dictionary<string, FieldMetadata> cache,
        ref long retainedBytes, int maxScheduledIds, CancellationToken cancellationToken)
    {
        if (document is not IFieldMetadataDocument source) return;
        var pending = new Queue<string>(ids);
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
            }
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var id in batch.Where(id => !next.ContainsKey(id)))
            {
                if (cache.Remove(id, out var removed))
                    retainedBytes -= MetadataCost(removed);
            }

            var admissions = new List<(string Id, FieldMetadata Field, long Delta, bool Changed)>(next.Count);
            foreach (var (id, field) in next)
            {
                var previous = cache.GetValueOrDefault(id);
                // Estimate the incoming graph before wire serialization/equality. A warm-cache
                // replacement can be much larger than the retained descriptor, so Agree must not
                // allocate its UTF-8 comparison buffers before aggregate admission.
                var incomingCost = MetadataCost(field);
                var previousCost = previous is null ? 0 : MetadataCost(previous);
                var delta = incomingCost - previousCost;
                if (delta > 0 && _maxMetadataBytes is { } limit && retainedBytes > limit - delta)
                    throw new InvalidOperationException("Field metadata exceeds the bounded review budget.");
                var changed = admitSemantic is not null || previous is null || !Agree(previous, field);
                admissions.Add((id, field, delta, changed));
                retainedBytes += delta;
            }

            // Validate live bindings only after the whole batch has been admitted. A descriptor
            // that cannot fit the remaining aggregate budget must not trigger a plugin getter.
            if (admitSemantic is null)
                foreach (var (_, field, _, _) in admissions)
                    _ = document.GetValue(field.ValueId);
            foreach (var (id, field, _, changed) in admissions)
            {
                if (changed)
                    cache[id] = admitSemantic is null ? Snapshot(field) : field;
                touched.Add(id);
                fetched.Add(id);
            }

            foreach (var field in next.Values)
            {
                var canonicalId = Normalize(field).CanonicalValueId;
                if (canonicalId != field.ValueId && !fetched.Contains(canonicalId)
                    && scheduled.Add(canonicalId))
                {
                    if (scheduled.Count > maxScheduledIds)
                        throw new InvalidOperationException("Canonical metadata closure exceeded its bounded limit.");
                    pending.Enqueue(canonicalId);
                }
            }

            // A target that has arrived can reject an alias-of-derived relationship immediately;
            // no recursive chain traversal is needed. Final agreement waits until all batches finish.
            if (admitSemantic is null)
                foreach (var field in cache.Values.Where(field => touched.Contains(field.ValueId)))
                    if (HasAvailableTarget(field, fetched)) ValidateCanonical(field, cache);
        }
        // Semantic callers have an immutable entry snapshot and a separate full-payload staging
        // cap. Admit the whole closure before equality buffers, getters, or detached copies.
        admitSemantic?.Invoke(cache);
        foreach (var field in cache.Values.Where(field => touched.Contains(field.ValueId)
            || touched.Contains(Normalize(field).CanonicalValueId)))
            ValidateCanonical(field, cache);
        if (admitSemantic is not null)
            foreach (var id in touched)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = document.GetValue(id);
                cache[id] = Snapshot(cache[id]);
            }
        cancellationToken.ThrowIfCancellationRequested();
        publishSemantic?.Invoke();
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

    private static string[] MaterializeInitialIds(IEnumerable<string> ids, int limit)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var result = new List<string>(Math.Min(limit, 64));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!seen.Add(id)) continue;
            if (result.Count >= limit)
                throw new InvalidOperationException($"Field metadata request exceeds the bounded limit of {limit} IDs.");
            result.Add(id);
        }

        return result.ToArray();
    }

    private static long MetadataBytes(IEnumerable<FieldMetadata> fields)
    {
        long total = 0;
        foreach (var field in fields)
            total = checked(total + MetadataCost(field));
        return total;
    }

    private static long MetadataCost(FieldMetadata field) =>
        SemanticMetadataBudget.TryEstimate(field, long.MaxValue, out var bytes)
            ? bytes : throw new InvalidOperationException("Field metadata exceeds bounded snapshot limits.");

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
