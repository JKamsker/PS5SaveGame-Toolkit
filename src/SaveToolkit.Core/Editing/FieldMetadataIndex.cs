using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Editing;

/// <summary>Targeted, bounded discovery against the current document, never a rendered page.</summary>
internal sealed class FieldMetadataIndex(ISaveDocument? document)
{
    internal const int BatchSize = 200;
    private readonly Dictionary<string, FieldMetadata> _fields = new(StringComparer.Ordinal);
    public bool Enabled => document is IFieldMetadataDocument
        && (document is not SaveToolkit.Core.Sessions.Groups.StagedGroupDocument staged || staged.MetadataEnabled);
    public IEnumerable<string> Ids => _fields.Keys;
    public void Clear() => _fields.Clear();
    internal FieldMetadataIndex Copy(CancellationToken cancellationToken)
    {
        var copy = new FieldMetadataIndex(document);
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
        ResolveAll(ids, working, new HashSet<string>(StringComparer.Ordinal), cancellationToken);
        _fields.Clear();
        foreach (var (id, field) in working) _fields.Add(id, field);
    }

    private void ResolveAll(IEnumerable<string> ids, Dictionary<string, FieldMetadata> cache,
        HashSet<string> resolving, CancellationToken cancellationToken)
    {
        if (document is not IFieldMetadataDocument source) return;
        var requestedIds = ids.Distinct(StringComparer.Ordinal).Where(resolving.Add).ToArray();
        try
        {
            foreach (var batch in requestedIds.Chunk(BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    if (!cache.TryGetValue(id, out var previous) || !Agree(previous, field)) cache[id] = Snapshot(field);

                // Alias targets are resolved separately because the SDK contract returns only requested
                // descriptors. This remains bounded and never enumerates the document.
                var canonicalIds = next.Values
                    .Select(field => field.CanonicalValueId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Where(id => !next.ContainsKey(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (canonicalIds.Length > 0) ResolveAll(canonicalIds, cache, resolving, cancellationToken);
                foreach (var field in next.Values) ValidateCanonical(field, cache, new HashSet<string>(StringComparer.Ordinal));
            }
        }
        finally
        {
            foreach (var id in requestedIds) resolving.Remove(id);
        }
    }

    private static void ValidateCanonical(FieldMetadata field, IReadOnlyDictionary<string, FieldMetadata> cache,
        HashSet<string> visiting)
    {
        var canonicalId = string.IsNullOrEmpty(field.CanonicalValueId) ? field.ValueId : field.CanonicalValueId;
        if (canonicalId == field.ValueId)
        {
            if (field.BindingKind == FieldBindingKind.Primary && field.Spec.ReadOnly)
                throw new InvalidOperationException($"Canonical primary '{field.ValueId}' is read-only.");
            return;
        }
        if (!visiting.Add(field.ValueId))
            throw new InvalidOperationException($"Canonical field alias cycle includes '{field.ValueId}'.");
        try
        {
            if (field.BindingKind != FieldBindingKind.Derived || !field.Spec.ReadOnly
                || !cache.TryGetValue(canonicalId, out var canonical)
                || canonical.BindingKind != FieldBindingKind.Primary
                || canonical.Spec.ReadOnly
                || (string.IsNullOrEmpty(canonical.CanonicalValueId) ? canonical.ValueId : canonical.CanonicalValueId) != canonical.ValueId
                || !FieldSpecMetadata.Agree(field.Spec, canonical.Spec with { ReadOnly = true }))
                throw new InvalidOperationException($"Invalid canonical alias '{field.ValueId}'.");
            ValidateCanonical(canonical, cache, visiting);
        }
        finally { visiting.Remove(field.ValueId); }
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
