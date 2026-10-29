using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.AtomicHeart;

internal static class PrimitivePayload
{
    internal static void ValidateSize(string type, string? detail, int size)
    {
        int? expected = type switch
        {
            "BoolProperty" => 0,
            "IntProperty" or "UInt32Property" or "FloatProperty" => sizeof(int),
            "Int64Property" or "UInt64Property" or "DoubleProperty" => sizeof(long),
            "StructProperty" when detail is "Timespan" or "DateTime" => sizeof(long),
            _ => null,
        };
        if (expected is { } required && size != required)
        {
            var label = string.IsNullOrEmpty(detail) ? type : $"{type} ({detail})";
            throw new SaveFormatException($"Invalid {label} payload size: expected {required} bytes, got {size}.");
        }
    }
}
