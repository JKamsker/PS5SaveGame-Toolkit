using System.Text.Json;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>A strict, local-only subset of JSON Schema for explicitly supplied insertion templates.</summary>
internal sealed record JsonTemplateSchema(string Type, IReadOnlyDictionary<string, JsonTemplateSchema> Properties,
    IReadOnlyList<string> Required, JsonTemplateSchema? Items, int MinItems, int MaxItems, bool AdditionalProperties, string? Default)
{
    internal const int MaxSchemaLength = 64 * 1024;
    private const int MaxSchemaDepth = 16;
    private const int MaxGeneratedItems = 128;
    private const int MaxGeneratedNodes = 4096;

    public static JsonTemplateSchema Parse(string text)
    {
        if (text.Length > MaxSchemaLength) throw new ArgumentException("Template schema exceeds 64 KiB of text.");
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = MaxSchemaDepth });
            var schema = Read(document.RootElement);
            return schema;
        }
        catch (JsonException) { throw new ArgumentException("Invalid template schema JSON (maximum depth 16)."); }
    }

    public string Generate()
    {
        var remaining = MaxGeneratedNodes;
        var json = Build(this, ref remaining);
        var parsed = JsonTokenTree.Parse(json);
        if (parsed.Nodes.Count > MaxGeneratedNodes) throw new ArgumentException("Generated template exceeds the 4,096 node budget.");
        Validate(parsed.Nodes[0]);
        return json;
    }

    public void Validate(JsonValueToken value)
    {
        var type = value.Kind switch
        {
            JsonTokenType.StartObject => "object",
            JsonTokenType.StartArray => "array",
            _ => value.Spec?.ReadOnly == true ? "number" : JsonScalar.TypeOf(value.Current),
        };
        if (type != Type) throw new ArgumentException("Inserted JSON does not match the explicit template schema type.");
        if (Type == "object")
        {
            if (value.Children.Select(n => n.Label).Distinct(StringComparer.Ordinal).Count() != value.Children.Count)
                throw new ArgumentException("Schema-validated templates cannot contain ambiguous duplicate members.");
            if (Required.Any(key => !value.Children.Any(n => n.Label == key)))
                throw new ArgumentException("Inserted object is missing a schema-required member.");
            foreach (var child in value.Children)
                if (Properties.TryGetValue(child.Label, out var property)) property.Validate(child);
                else if (!AdditionalProperties) throw new ArgumentException("Inserted object has a member outside the explicit template schema.");
        }
        if (Type == "array")
        {
            if (value.Children.Count < MinItems || value.Children.Count > MaxItems)
                throw new ArgumentException("Inserted array is outside the explicit schema item-count bounds.");
            foreach (var child in value.Children) Items?.Validate(child);
        }
    }

    private static JsonTemplateSchema Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new ArgumentException("Every template schema must be an object with one explicit type.");
        var fields = Members(element);
        if (!fields.TryGetValue("type", out var typeValue) || typeValue.ValueKind != JsonValueKind.String)
            throw new ArgumentException("Template schemas require one explicit JSON type.");
        var type = typeValue.GetString()!;
        if (type is not ("object" or "array" or "string" or "number" or "boolean" or "null"))
            throw new ArgumentException("Unsupported template schema type.");
        foreach (var key in fields.Keys)
            if (key is not ("type" or "default") && !(type == "object" && key is "properties" or "required" or "additionalProperties")
                && !(type == "array" && key is "items" or "minItems" or "maxItems"))
                throw new ArgumentException("Unsupported template schema keyword; only the documented local subset is accepted.");
        var properties = fields.TryGetValue("properties", out var propertyMap)
            ? Members(propertyMap).ToDictionary(p => p.Key, p => Read(p.Value), StringComparer.Ordinal) : new Dictionary<string, JsonTemplateSchema>(StringComparer.Ordinal);
        var required = new List<string>();
        if (fields.TryGetValue("required", out var requiredValue))
        {
            if (requiredValue.ValueKind != JsonValueKind.Array) throw new ArgumentException("Schema required must be an array of unique property names.");
            foreach (var item in requiredValue.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || required.Contains(item.GetString()!, StringComparer.Ordinal))
                    throw new ArgumentException("Schema required must be an array of unique property names.");
                required.Add(item.GetString()!);
            }
            // Required undeclared members are valid in general JSON Schema, but cannot be generated honestly.
            if (required.Any(key => !properties.ContainsKey(key)))
                throw new ArgumentException("This template subset requires every required property to have an explicit property schema.");
        }
        var additional = true;
        if (fields.TryGetValue("additionalProperties", out var additionalValue))
        {
            if (additionalValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ArgumentException("This template subset accepts only boolean additionalProperties.");
            additional = additionalValue.GetBoolean();
        }
        var minimum = Count(fields, "minItems", 0);
        var maximum = Count(fields, "maxItems", JsonTokenTree.MaxNodes);
        if (minimum > maximum) throw new ArgumentException("Schema minItems must not exceed maxItems.");
        return new(type, properties, required, fields.TryGetValue("items", out var items) ? Read(items) : null,
            minimum, maximum, additional, fields.TryGetValue("default", out var initial) ? initial.GetRawText() : null);
    }

    private static Dictionary<string, JsonElement> Members(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new ArgumentException("Schema properties must be an object.");
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var member in element.EnumerateObject())
            if (!result.TryAdd(member.Name, member.Value)) throw new ArgumentException("Duplicate schema members are ambiguous.");
        return result;
    }

    private static int Count(IReadOnlyDictionary<string, JsonElement> fields, string key, int fallback)
    {
        if (!fields.TryGetValue(key, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < 0 || number > JsonTokenTree.MaxNodes)
            throw new ArgumentException("Schema item counts must be nonnegative integers within the JSON node limit.");
        return number;
    }

    private static string Build(JsonTemplateSchema schema, ref int remaining)
    {
        if (--remaining < 0) throw new ArgumentException("Generated template exceeds the 4,096 node budget; supply a smaller explicit default.");
        if (schema.Default is { } value) return value;
        if (schema.Type == "object")
        {
            var members = new List<string>();
            foreach (var key in schema.Required)
                members.Add(JsonSerializer.Serialize(key) + ":" + Build(schema.Properties[key], ref remaining));
            return Container("{", members, "}");
        }
        if (schema.Type == "array")
        {
            if (schema.MinItems > MaxGeneratedItems || schema.MinItems > 0 && schema.Items is null)
                throw new ArgumentException("Array generation needs an explicit item schema and at most 128 required items; supply an explicit default otherwise.");
            var items = new List<string>();
            for (var i = 0; i < schema.MinItems; i++) items.Add(Build(schema.Items!, ref remaining));
            return Container("[", items, "]");
        }
        return schema.Type switch { "string" => "\"\"", "number" => "0", "boolean" => "false", _ => "null" };
    }

    private static string Container(string opening, IReadOnlyList<string> parts, string closing)
    {
        if (parts.Sum(part => (long)part.Length) + parts.Count + 2 > JsonTemplates.MaxInsertedText)
            throw new ArgumentException("Generated JSON template exceeds 1 MiB of text.");
        return opening + string.Join(',', parts) + closing;
    }
}
