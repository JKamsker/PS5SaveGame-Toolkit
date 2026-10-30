using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.XorJson;

internal static class JsonMetadataRequest
{
    public static void Validate(IReadOnlyList<string> valueIds)
    {
        ArgumentNullException.ThrowIfNull(valueIds);
        if (valueIds.Count > FieldMetadataLimits.MaxBatchSize)
            throw new ArgumentOutOfRangeException(nameof(valueIds), valueIds.Count,
                $"Request at most {FieldMetadataLimits.MaxBatchSize} field IDs per batch.");
        foreach (var valueId in valueIds)
            ArgumentException.ThrowIfNullOrEmpty(valueId, nameof(valueIds));
    }
}
