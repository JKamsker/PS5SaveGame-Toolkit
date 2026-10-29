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
        return metadata with { Path = metadata.Path is { } path ? Array.AsReadOnly(path.ToArray()) : null };
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
        foreach (var field in fields)
        {
            var (bindingSpec, hasSetter) = lookup(field.ValueId);
            var effectiveSpec = hasSetter ? bindingSpec : bindingSpec with { ReadOnly = true };
            if (!FieldSpecMetadata.Agree(effectiveSpec, field.Spec)
                || (field.BindingKind == FieldBindingKind.Derived && hasSetter))
                throw new InvalidOperationException($"Published metadata no longer agrees with binding '{field.ValueId}'.");
        }
        return Array.AsReadOnly(fields);
    }

    public void Clear() => _fields.Clear();
}
