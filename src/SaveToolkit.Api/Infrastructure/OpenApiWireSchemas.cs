using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>Custom JSON converters cannot expose their wire shapes to the default schema exporter.</summary>
internal sealed class OpenApiWireSchemas : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        // Nullable properties are omitted by ToolkitJson when absent. Constructor parameters
        // otherwise appear required in exported schemas, including rejected previews' planId.
        if (schema.Required is not null)
            schema.Required.ExceptWith(context.JsonTypeInfo.Properties
                .Where(property => !property.IsRequired && (property.IsGetNullable || Nullable.GetUnderlyingType(property.PropertyType) is not null))
                .Select(property => property.Name));
        if (context.JsonTypeInfo.Type == typeof(Int128) && context.JsonPropertyInfo?.CustomConverter is Int128StringConverter)
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = null;
            schema.Pattern = "^-?[0-9]+$";
        }
        if (context.JsonTypeInfo.Type == typeof(FieldValue))
        {
            schema.Type = null;
            schema.Description = "FieldSpec determines the value type. Integers use decimal strings to preserve Int128 precision.";
            schema.AnyOf =
            [
                new OpenApiSchema { Type = JsonSchemaType.Null },
                new OpenApiSchema { Type = JsonSchemaType.Boolean },
                new OpenApiSchema { Type = JsonSchemaType.String },
                new OpenApiSchema { Type = JsonSchemaType.Number },
                Tagged("exactNumber", new Dictionary<string, IOpenApiSchema>
                {
                    ["text"] = new OpenApiSchema { Type = JsonSchemaType.String },
                }),
                Tagged("union", new Dictionary<string, IOpenApiSchema>
                {
                    ["type"] = new OpenApiSchema { Type = JsonSchemaType.String },
                    ["value"] = new OpenApiSchemaReference(nameof(FieldValue), context.Document),
                }),
            ];
            context.Document?.AddComponent(nameof(FieldValue), schema);
        }
        return Task.CompletedTask;
    }

    private static OpenApiSchema Tagged(string kind, Dictionary<string, IOpenApiSchema> properties)
    {
        properties["kind"] = new OpenApiSchema { Type = JsonSchemaType.String, Enum = [JsonValue.Create(kind)!] };
        return new OpenApiSchema { Type = JsonSchemaType.Object, Properties = properties, Required = properties.Keys.ToHashSet(StringComparer.Ordinal) };
    }
}
