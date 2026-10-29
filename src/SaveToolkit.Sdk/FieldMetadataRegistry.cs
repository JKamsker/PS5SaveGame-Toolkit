using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Sdk;

internal sealed class FieldMetadataRegistry
{
    private readonly Dictionary<string, FieldMetadata> _fields = new(StringComparer.Ordinal);

    public static FieldMetadata Prepare(FieldMetadata metadata, Func<FieldValue> get, Action<FieldValue>? set)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(metadata.Spec);
        ArgumentNullException.ThrowIfNull(get);
        ArgumentException.ThrowIfNullOrEmpty(metadata.ValueId);
        ArgumentException.ThrowIfNullOrEmpty(metadata.Label);
        if (!Enum.IsDefined(metadata.BindingKind) || (!metadata.Spec.ReadOnly && set is null)
            || (metadata.BindingKind == FieldBindingKind.Derived && (!metadata.Spec.ReadOnly || set is not null)))
            throw new ArgumentException("Metadata requires a writable primary or a read-only binding; derived fields cannot have setters.", nameof(metadata));
        if (metadata.Presentation?.Confidence is { } confidence && !Enum.IsDefined(confidence))
            throw new ArgumentException("Field presentation has an unknown confidence.", nameof(metadata));
        var canonical = string.IsNullOrEmpty(metadata.CanonicalValueId) ? metadata.ValueId : metadata.CanonicalValueId;
        if (metadata.BindingKind == FieldBindingKind.Primary && canonical != metadata.ValueId)
            throw new ArgumentException("Primary metadata must use its own value ID as the canonical ID.", nameof(metadata));
        return metadata with
        {
            CanonicalValueId = canonical,
            Path = metadata.Path is { } path ? Array.AsReadOnly(path.ToArray()) : null,
        };
    }

    public void Register(FieldMetadata metadata) => _fields[metadata.ValueId] = metadata;

    public IReadOnlyList<FieldMetadata> Resolve(IReadOnlyList<string> valueIds,
        Func<string, (FieldSpec Spec, bool HasSetter)> lookup)
    {
        ArgumentNullException.ThrowIfNull(valueIds);
        if (valueIds.Count > FieldMetadataLimits.MaxBatchSize)
            throw new ArgumentOutOfRangeException(nameof(valueIds), valueIds.Count,
                $"Request at most {FieldMetadataLimits.MaxBatchSize} field IDs per batch.");
        foreach (var id in valueIds) ArgumentException.ThrowIfNullOrEmpty(id, nameof(valueIds));

        var fields = valueIds.Distinct(StringComparer.Ordinal).Where(_fields.ContainsKey).Select(id => _fields[id]).ToArray();
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            Validate(field);
        }
        return Array.AsReadOnly(fields);

        void Validate(FieldMetadata field)
        {
            if (!visiting.Add(field.ValueId))
                throw new InvalidOperationException($"Canonical field alias cycle includes '{field.ValueId}'.");
            try
            {
                var (bindingSpec, hasSetter) = lookup(field.ValueId);
                var effectiveSpec = hasSetter ? bindingSpec : bindingSpec with { ReadOnly = true };
                if (!FieldSpecMetadata.Agree(effectiveSpec, field.Spec)
                    || (field.BindingKind == FieldBindingKind.Derived && hasSetter))
                    throw new InvalidOperationException($"Published metadata no longer agrees with binding '{field.ValueId}'.");

                if (field.CanonicalValueId == field.ValueId)
                {
                    if (field.BindingKind == FieldBindingKind.Primary && !field.Spec.ReadOnly && !hasSetter)
                        throw new InvalidOperationException($"Canonical field '{field.ValueId}' must be writable.");
                    return;
                }

                if (field.BindingKind != FieldBindingKind.Derived || !field.Spec.ReadOnly)
                    throw new InvalidOperationException($"Only read-only derived fields may alias '{field.CanonicalValueId}'.");
                if (!_fields.TryGetValue(field.CanonicalValueId, out var canonical))
                    throw new InvalidOperationException($"Canonical field '{field.CanonicalValueId}' is not published.");
                var (canonicalBindingSpec, canonicalHasSetter) = lookup(canonical.ValueId);
                if (canonical.BindingKind != FieldBindingKind.Primary || canonical.Spec.ReadOnly || !canonicalHasSetter
                    || !FieldSpecMetadata.Agree(canonicalBindingSpec, canonical.Spec))
                    throw new InvalidOperationException($"Canonical field '{field.CanonicalValueId}' is not a writable primary.");
                if (!FieldSpecMetadata.Agree(field.Spec, canonical.Spec with { ReadOnly = true }))
                    throw new InvalidOperationException($"Alias '{field.ValueId}' disagrees with canonical field '{field.CanonicalValueId}'.");
                Validate(canonical);
            }
            finally { visiting.Remove(field.ValueId); }
        }
    }

    public void Clear() => _fields.Clear();
}
