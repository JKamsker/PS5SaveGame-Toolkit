using System.Text.Json;

namespace SaveToolkit.Sdk.Values;

/// <summary>Tagged additions to the protocol; existing primitive wire representations are unchanged.</summary>
internal static class TaggedFieldValueJson
{
    public static FieldValue Read(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var json = document.RootElement;
        try
        {
            var kind = json.GetProperty("kind").GetString();
            return kind switch
            {
                "exactNumber" when HasProperties(json, "kind", "text") => FieldValue.ExactNumber(json.GetProperty("text").GetString()!),
                "union" when HasProperties(json, "kind", "type", "value") => FieldValue.Union(
                    json.GetProperty("type").GetString()!, json.GetProperty("value").Deserialize<FieldValue>(options)),
                _ => throw new JsonException("Expected a tagged exactNumber or union value."),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            throw new JsonException("Invalid tagged field value.", ex);
        }
    }

    public static void Write(Utf8JsonWriter writer, FieldValue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Kind == FieldValueKind.ExactNumber)
        {
            writer.WriteString("kind", "exactNumber");
            writer.WriteString("text", value.AsExactNumber());
        }
        else
        {
            var union = value.AsUnion();
            writer.WriteString("kind", "union");
            writer.WriteString("type", union.Type);
            writer.WritePropertyName("value");
            JsonSerializer.Serialize(writer, union.Value, options);
        }

        writer.WriteEndObject();
    }

    private static bool HasProperties(JsonElement json, params string[] names) =>
        json.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(names.Order());
}
