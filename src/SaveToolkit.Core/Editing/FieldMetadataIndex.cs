using System.Runtime.CompilerServices;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Editing;

/// <summary>Targeted, bounded discovery against the current document, never a rendered page.</summary>
internal sealed class FieldMetadataIndex(ISaveDocument? document, long? maxMetadataBytes = null,
    Action<IReadOnlyDictionary<string, FieldMetadata>, IReadOnlySet<string>>? admitSemantic = null, Action? publishSemantic = null)
{
    internal const int BatchSize = 200;
    private FieldMetadataCache _cache = FieldMetadataCache.Empty;
    private long? _maxMetadataBytes = maxMetadataBytes;
    // Retained entries are detached snapshots, so an entry's cost never changes. Memoised per
    // instance (weakly, so replaced entries are not kept alive): batched callers resolve a large
    // closure one batch at a time, and re-estimating every retained entry per batch is quadratic.
    private readonly ConditionalWeakTable<FieldMetadata, StrongBox<long>> _retainedCosts = new();
    public bool Enabled => document is IFieldMetadataDocument
        && (document is not SaveToolkit.Core.Sessions.Groups.StagedGroupDocument staged || staged.MetadataEnabled);
    public IEnumerable<string> Ids => _cache.Fields.Keys;
    public void Clear() => _cache = FieldMetadataCache.Empty;
    internal void SetBudget(long? bytes) => _maxMetadataBytes = bytes;
    internal FieldMetadataIndex Copy(CancellationToken cancellationToken)
    {
        var copy = new FieldMetadataIndex(document, _maxMetadataBytes, admitSemantic, publishSemantic);
        cancellationToken.ThrowIfCancellationRequested();
        copy._cache = _cache;
        return copy;
    }
    public FieldMetadata? Cached(string id) => _cache.Fields.GetValueOrDefault(id);

    internal IReadOnlyList<FieldMetadata> ResolveForPlanning(IReadOnlyList<string> ids, int maximumIds,
        long allowance, CancellationToken cancellationToken)
    {
        if (document is null || !Enabled) return [];
        var closure = PlanningMetadataAdmission.Resolve(document, ids, maximumIds, allowance, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var cache = _cache.ToBuilder();
        foreach (var id in ids) cache.Remove(id, out _);
        foreach (var field in closure) cache.Set(field.ValueId, field);
        _cache = cache.ToImmutable();
        return closure;
    }

    public FieldMetadata? Resolve(string id)
    {
        ResolveAll([id]);
        return Cached(id);
    }

    /// <summary>Returns the explicitly published canonical primary for an ID, if one is known.</summary>
    public FieldMetadata? Canonical(string id)
    {
        ResolveAll([id]);
        var field = Cached(id);
        var canonicalId = field is null || string.IsNullOrEmpty(field.CanonicalValueId)
            ? field?.ValueId : field.CanonicalValueId;
        if (field is null || canonicalId == field.ValueId) return field;
        ResolveAll([canonicalId!]);
        return Cached(canonicalId!);
    }

    public void ResolveAll(IEnumerable<string> ids, CancellationToken cancellationToken = default)
        => ResolveAll(ids, FieldMetadataLimits.MaxClosureIds, cancellationToken);

    internal void ResolveAll(IEnumerable<string> ids, int maxInitialIds, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxInitialIds);
        var initial = MaterializeInitialIds(ids, maxInitialIds);
        // Copy-on-write: a failed resolution discards the builder and leaves the cache untouched,
        // and a successful one costs O(batch log n), not a copy of every field resolved so far.
        var working = _cache.ToBuilder();
        var retainedBytes = _maxMetadataBytes is null ? 0 : MetadataBytes(working.Fields.Values);
        ResolveAll(initial, working, ref retainedBytes, maxInitialIds, cancellationToken);
        _cache = working.ToImmutable();
    }

    private void ResolveAll(IReadOnlyList<string> ids, FieldMetadataCache.Builder cache,
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
                var previous = cache.Fields.GetValueOrDefault(id);
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
                    cache.Set(id, admitSemantic is null ? Snapshot(field) : field);
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
                foreach (var id in touched)
                    if (cache.Fields.TryGetValue(id, out var field) && HasAvailableTarget(field, fetched))
                        ValidateCanonical(field, cache.Fields);
        }
        // Semantic callers have an immutable entry snapshot and a separate full-payload staging
        // cap. Admit the whole closure before equality buffers, getters, or detached copies.
        // The second argument names this resolution's fresh descriptors; all others are retained snapshots.
        admitSemantic?.Invoke(cache.Fields, touched);
        // Every touched entry, plus each cached alias whose canonical target was touched.
        var revalidate = new HashSet<string>(touched, StringComparer.Ordinal);
        foreach (var id in touched) revalidate.UnionWith(cache.AliasesOf(id));
        foreach (var id in revalidate)
            if (cache.Fields.TryGetValue(id, out var field))
                ValidateCanonical(field, cache.Fields);
        if (admitSemantic is not null)
            foreach (var id in touched)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = document.GetValue(id);
                cache.Set(id, Snapshot(cache.Fields[id]));
            }
        cancellationToken.ThrowIfCancellationRequested();
        publishSemantic?.Invoke();
    }

    private static bool HasAvailableTarget(FieldMetadata field, IReadOnlySet<string> fetched) =>
        Normalize(field).CanonicalValueId == field.ValueId || fetched.Contains(Normalize(field).CanonicalValueId);

    internal static void ValidateCanonical(FieldMetadata field, IReadOnlyDictionary<string, FieldMetadata> cache)
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

    private long MetadataBytes(IEnumerable<FieldMetadata> fields)
    {
        long total = 0;
        foreach (var field in fields)
        {
            if (!_retainedCosts.TryGetValue(field, out var cost))
            {
                cost = new StrongBox<long>(MetadataCost(field));
                _retainedCosts.AddOrUpdate(field, cost);
            }
            total = checked(total + cost.Value);
        }
        return total;
    }

    private static long MetadataCost(FieldMetadata field) =>
        SemanticMetadataBudget.TryEstimate(field, long.MaxValue, out var bytes)
            ? bytes : throw new InvalidOperationException("Field metadata exceeds bounded snapshot limits.");

    // Copy nested option/union lists too: plugin-owned mutable collections must not alter a preview.
    internal static FieldMetadata Snapshot(FieldMetadata field) => field with
    {
        Spec = FieldSpecMetadata.Snapshot(field.Spec),
        Path = field.Path is { } path ? Array.AsReadOnly(path.ToArray()) : null,
    };

    internal static bool Agree(FieldMetadata first, FieldMetadata second) =>
        FieldSpecMetadata.Agree(first.Spec, second.Spec) && WireAgree(first, second);

    internal static bool Agree(IReadOnlyList<FieldMetadata> first, IReadOnlyList<FieldMetadata> second) =>
        first.Count == second.Count && first.Zip(second).All(pair => Agree(pair.First, pair.Second));

    private static bool WireAgree<T>(T first, T second) =>
        JsonSerializer.SerializeToUtf8Bytes(first, MetadataJson.Options).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(second, MetadataJson.Options));
}
