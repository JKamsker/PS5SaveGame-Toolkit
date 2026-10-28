using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveToolkit.Core;

/// <summary>The JSON shape used on the wire (camelCase, string enums, nulls omitted).</summary>
public static class ToolkitJson
{
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter());
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return options;
    }
}
