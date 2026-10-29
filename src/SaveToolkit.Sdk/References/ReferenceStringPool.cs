using System.Collections.ObjectModel;

namespace SaveToolkit.Sdk.References;

/// <summary>
/// Shares ordinally identical record text across datasets/locales owned by one loaded context.
/// Unlike string.Intern, the pool is collectible with that context. Validation does not populate it.
/// </summary>
internal sealed class ReferenceStringPool
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);

    public ReferenceRecord ToRecord(RecordModel record, string packId, ReferenceDatasetInfo dataset)
    {
        // One lock per record, rather than per string. Different packs may load concurrently.
        lock (_gate)
        {
            return new(Share(record.Id!), Share(record.Name!.Trim()))
            {
                Category = Optional(record.Category),
                Limit = record.Limit,
                Description = Optional(record.Description),
                AssetId = record.AssetId is null ? null : Share(PackPaths.QualifyAsset(packId, record.AssetId)),
                Source = Optional(record.Source),
                Attributes = record.Attributes is { } attributes
                    ? attributes.ToDictionary(pair => Share(pair.Key), pair => Share(pair.Value), StringComparer.Ordinal).AsReadOnly()
                    : ReadOnlyDictionary<string, string>.Empty,
                Dataset = dataset,
            };
        }
    }

    private string? Optional(string? value) => value is null ? null : Share(value);

    // Called only under _gate; nothing is added until a dataset is actually materialized.
    private string Share(string value)
    {
        if (_strings.TryGetValue(value, out var shared)) return shared;
        _strings.Add(value, value);
        return value;
    }
}
