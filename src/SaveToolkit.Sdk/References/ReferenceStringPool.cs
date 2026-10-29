using System.Collections.ObjectModel;
using System.Text.Json;

namespace SaveToolkit.Sdk.References;

/// <summary>
/// Shares ordinally identical record text across datasets/locales owned by one loaded context.
/// Unlike string.Intern, the pool is collectible with that context. Validation does not populate it.
/// </summary>
internal sealed class ReferenceStringPool
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private readonly Lazy<JsonSerializerOptions> _jsonOptions;

    public ReferenceStringPool()
    {
        _jsonOptions = new(() =>
        {
            var options = new JsonSerializerOptions(PackJson.Options);
            options.Converters.Add(new PooledReferenceStringConverter(this));
            return options;
        });
    }

    public JsonSerializerOptions JsonOptions => _jsonOptions.Value;

    public string Get(ReadOnlySpan<char> value)
    {
        lock (_gate)
        {
            // Ordinal alternate lookup avoids allocating a temporary string for repeated JSON text.
            var lookup = _strings.GetAlternateLookup<ReadOnlySpan<char>>();
            if (lookup.TryGetValue(value, out var shared)) return shared;
            var owned = new string(value);
            _strings.Add(owned, owned);
            return owned;
        }
    }

    public ReferenceRecord ToRecord(RecordModel record, string packId, ReferenceDatasetInfo dataset, bool attributesAreShared)
    {
        // Hold the pool lock for the complete record conversion; different packs may load concurrently.
        lock (_gate)
        {
            return new(Share(record.Id!), Share(record.Name!.Trim()))
            {
                Category = Optional(record.Category),
                Limit = record.Limit,
                Description = Optional(record.Description),
                AssetId = record.AssetId is null ? null : Share(PackPaths.QualifyAsset(packId, record.AssetId)),
                Source = Optional(record.Source),
                Attributes = ShareAttributes(record.Attributes, attributesAreShared),
                Dataset = dataset,
            };
        }
    }

    private IReadOnlyDictionary<string, string> ShareAttributes(Dictionary<string, string>? attributes, bool alreadyShared)
    {
        if (attributes is null) return ReadOnlyDictionary<string, string>.Empty;
        if (!alreadyShared)
            attributes = attributes.ToDictionary(pair => Share(pair.Key), pair => Share(pair.Value), StringComparer.Ordinal);
        return attributes.AsReadOnly();
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
