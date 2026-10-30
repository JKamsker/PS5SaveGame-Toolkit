using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Core.Presets;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>Describes strict package parameters without changing legacy CRUD schemas or runtime parsing.</summary>
internal sealed class OpenApiPresetPackageSchemas : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        // Reconnect the recursive value component after export, as OpenApiPolymorphismSchemas does.
        // The .NET 10.0.14 exporter cannot safely walk this recursive reference in a schema callback.
        if (document.Components!.Schemas!.TryGetValue(nameof(PresetPackageActionDto), out var action) && action is OpenApiSchema schema)
        {
            schema.Properties!["parameters"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object, MaxProperties = 128,
                AdditionalProperties = new OpenApiSchemaReference(nameof(FieldValue), document),
                Description = "At most 128 parameter IDs (1–256 UTF-16 units, nonblank, no controls). " +
                    "Numbers retain exact JSON lexemes; strings and tagged values are bounded to 4096 units. Arrays and loose save objects are rejected.",
            };
        }
        if (document.Components.Schemas.TryGetValue(nameof(PresetPackageCompatibilityDto), out var compatibility)
            && compatibility is OpenApiSchema boundaries)
        {
            boundaries.Properties!["titleIds"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Array, MinItems = 1, MaxItems = PresetContractValidation.MaxMapEntries,
                Items = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = PresetContractValidation.MaxKeyLength },
            };
        }
        return Task.CompletedTask;
    }
}
