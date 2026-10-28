using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveToolkit.Testing.Diff;

/// <summary>JSON settings for manifests (lenient reading) and reports (camelCase, indented, nulls omitted).</summary>
public static class DiffJson
{
    public static JsonSerializerOptions ReadOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    public static JsonSerializerOptions WriteOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, WriteOptions);
}
