using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>Preserves recursive SDK schemas when the first-party exporter encounters polymorphic JSON pointers.</summary>
internal sealed class OpenApiPolymorphismSchemas(IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        // ASP.NET Core 10.0.14 cannot resolve exporter pointers that pass through anyOf arrays.
        // https://github.com/dotnet/aspnetcore/blob/v10.0.14/src/OpenApi/src/Services/Schemas/OpenApiSchemaService.cs#L511
        // Generate each type independently and reconnect its properties through public JSON metadata.
        // This affects documentation only; the application's serializers and handlers are unchanged.
        var graph = new SchemaGraph(document, context, jsonOptions.Value.SerializerOptions, cancellationToken);
        await graph.ReferenceAsync(typeof(FieldSpec));
        await graph.ReferenceAsync(typeof(ViewNode));
    }

    private sealed class SchemaGraph(OpenApiDocument document, OpenApiDocumentTransformerContext context,
        JsonSerializerOptions serializerOptions, CancellationToken cancellationToken)
    {
        private readonly Dictionary<Type, string> _componentNames = [];

        internal async Task<OpenApiSchemaReference> ReferenceAsync(Type type)
        {
            if (_componentNames.TryGetValue(type, out string? existing))
                return new OpenApiSchemaReference(existing, document);

            JsonTypeInfo typeInfo = serializerOptions.GetTypeInfo(type);
            string name = OpenApiOptions.CreateDefaultSchemaReferenceId(typeInfo) ?? type.Name;
            _componentNames.Add(type, name);
            // Register before following properties so recursive references terminate at this component.
            document.AddComponent(name, new OpenApiSchema());
            OpenApiSchema schema = await context.GetOrCreateSchemaAsync(type, cancellationToken: cancellationToken);
            document.Components!.Schemas![name] = schema;
            await ConnectPropertiesAsync(schema, typeInfo);
            if (typeInfo.PolymorphismOptions is { } polymorphism)
                await ConnectDerivedTypesAsync(schema, name, polymorphism);
            return new OpenApiSchemaReference(name, document);
        }

        private async Task ConnectPropertiesAsync(OpenApiSchema schema, JsonTypeInfo typeInfo)
        {
            if (schema.Properties is null) return;
            foreach (JsonPropertyInfo property in typeInfo.Properties)
            {
                if (!schema.Properties.TryGetValue(property.Name, out IOpenApiSchema? current)) continue;
                IOpenApiSchema replacement = await ShapeAsync(property.PropertyType, current);
                if (property.IsGetNullable && serializerOptions.GetTypeInfo(property.PropertyType).Kind == JsonTypeInfoKind.Object)
                {
                    replacement = new OpenApiSchema
                    {
                        OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, replacement],
                    };
                }
                schema.Properties[property.Name] = replacement;
            }
        }

        private async Task<IOpenApiSchema> ShapeAsync(Type type, IOpenApiSchema? existing = null)
        {
            JsonTypeInfo typeInfo = serializerOptions.GetTypeInfo(type);
            if (typeInfo.Kind == JsonTypeInfoKind.Object || type.IsEnum)
                return await ReferenceAsync(type);

            OpenApiSchema schema = existing as OpenApiSchema
                ?? await context.GetOrCreateSchemaAsync(type, cancellationToken: cancellationToken);
            if (typeInfo.Kind == JsonTypeInfoKind.Enumerable && typeInfo.ElementType is { } itemType)
            {
                schema.Type = JsonSchemaType.Array | (schema.Type.GetValueOrDefault() & JsonSchemaType.Null);
                schema.Items = await ShapeAsync(itemType);
                schema.AnyOf = null;
                schema.OneOf = null;
            }
            else if (typeInfo.Kind == JsonTypeInfoKind.Dictionary && typeInfo.ElementType is { } valueType)
            {
                schema.Type = JsonSchemaType.Object | (schema.Type.GetValueOrDefault() & JsonSchemaType.Null);
                schema.AdditionalPropertiesAllowed = true;
                schema.AdditionalProperties = await ShapeAsync(valueType);
                schema.AnyOf = null;
                schema.OneOf = null;
            }
            return schema;
        }

        private async Task ConnectDerivedTypesAsync(OpenApiSchema schema, string baseName,
            JsonPolymorphismOptions polymorphism)
        {
            var branches = new List<IOpenApiSchema>();
            var mapping = new Dictionary<string, OpenApiSchemaReference>(StringComparer.Ordinal);
            foreach (JsonDerivedType derived in polymorphism.DerivedTypes)
            {
                await ReferenceAsync(derived.DerivedType);
                string standaloneName = _componentNames[derived.DerivedType];
                string branchName = baseName + standaloneName;
                var properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
                var required = new HashSet<string>(StringComparer.Ordinal);
                var branch = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    AllOf = [new OpenApiSchemaReference(standaloneName, document)],
                    Properties = properties,
                    Required = required,
                };
                if (derived.TypeDiscriminator is { } discriminator)
                {
                    string value = Convert.ToString(discriminator, System.Globalization.CultureInfo.InvariantCulture)!;
                    properties[polymorphism.TypeDiscriminatorPropertyName] = new OpenApiSchema
                    {
                        Type = discriminator is string ? JsonSchemaType.String : JsonSchemaType.Integer,
                        Enum = [JsonSerializer.SerializeToNode(discriminator)!],
                    };
                    required.Add(polymorphism.TypeDiscriminatorPropertyName);
                    mapping[value] = new OpenApiSchemaReference(branchName, document);
                }
                document.Components!.Schemas![branchName] = branch;
                branches.Add(new OpenApiSchemaReference(branchName, document));
            }
            schema.Type = JsonSchemaType.Object;
            schema.AnyOf = branches;
            schema.Properties = null;
            schema.Required = new HashSet<string>([polymorphism.TypeDiscriminatorPropertyName], StringComparer.Ordinal);
            schema.Discriminator = new OpenApiDiscriminator
            {
                PropertyName = polymorphism.TypeDiscriminatorPropertyName,
                Mapping = mapping,
            };
        }
    }
}
