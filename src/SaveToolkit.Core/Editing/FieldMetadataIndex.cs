using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Editing;

/// <summary>Targeted, bounded discovery against the current document, never a rendered page.</summary>
internal sealed class FieldMetadataIndex(ISaveDocument? document)
{
    internal const int BatchSize = 200;
    private readonly Dictionary<string, FieldMetadata> _fields = new(StringComparer.Ordinal);
    public bool Enabled => document is IFieldMetadataDocument;
    public IEnumerable<string> Ids => _fields.Keys;
    public void Clear() => _fields.Clear();
    public FieldMetadata? Cached(string id) => _fields.GetValueOrDefault(id);

    public FieldMetadata? Resolve(string id)
    {
        ResolveAll([id]);
        return _fields.GetValueOrDefault(id);
    }

    public void ResolveAll(IEnumerable<string> ids)
    {
        if (document is not IFieldMetadataDocument source) return;
        foreach (var batch in ids.Distinct(StringComparer.Ordinal).Chunk(BatchSize))
        {
            var requested = batch.ToHashSet(StringComparer.Ordinal);
            var resolved = source.ResolveFieldMetadata(Array.AsReadOnly(batch));
            if (resolved.Count > batch.Length) throw new InvalidOperationException("Too many field descriptors returned.");
            var next = new Dictionary<string, FieldMetadata>(StringComparer.Ordinal);
            foreach (var field in resolved)
            {
                if (string.IsNullOrEmpty(field.ValueId) || !requested.Contains(field.ValueId)
                    || string.IsNullOrEmpty(field.Label) || field.Spec is null
                    || !Enum.IsDefined(field.BindingKind)
                    || (field.BindingKind == FieldBindingKind.Derived && !field.Spec.ReadOnly)
                    || !next.TryAdd(field.ValueId, field))
                    throw new InvalidOperationException("Invalid or duplicate field metadata.");
                _ = document.GetValue(field.ValueId); // A descriptor promises a live binding.
            }

            foreach (var id in batch.Where(id => !next.ContainsKey(id))) _fields.Remove(id);
            foreach (var (id, field) in next)
                if (!_fields.TryGetValue(id, out var previous) || !Agree(previous, field)) _fields[id] = Snapshot(field);
        }
    }

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
