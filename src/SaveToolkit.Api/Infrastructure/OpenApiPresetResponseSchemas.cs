using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using SaveToolkit.Api.Contracts;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>Describes both preset adapters and their retained preview/edit failure envelopes.</summary>
internal sealed class OpenApiPresetResponseSchemas : IOpenApiDocumentTransformer
{
    private const string PresetRoute = "/api/workspaces/{workspaceId}/documents/{documentId}/presets/{presetId}";

    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var semantic = await context.GetOrCreateSchemaAsync(typeof(PresetSemanticPreviewDto), cancellationToken: cancellationToken);
        document.AddComponent(nameof(PresetSemanticPreviewDto), semantic);
        var preview = document.Paths[PresetRoute + "/preview"].Operations![HttpMethod.Post];
        preview.Responses!["200"].Content!["application/json"].Schema = new OpenApiSchema
        {
            AnyOf =
            [
                new OpenApiSchemaReference(nameof(PresetPreviewDto), document),
                new OpenApiSchemaReference(nameof(PresetSemanticPreviewDto), document),
            ],
        };
        DescribeErrors(preview);
        DescribeErrors(document.Paths[PresetRoute + "/apply"].Operations![HttpMethod.Post]);
    }

    private static void DescribeErrors(OpenApiOperation operation)
    {
        var success = operation.Responses!["200"].Content!["application/json"].Schema!;
        foreach (var status in new[] { "409", "422" })
        {
            var error = operation.Responses[status].Content!["application/json"];
            error.Schema = new OpenApiSchema
            {
                AnyOf = [error.Schema!, new OpenApiSchema { AllOf = [error.Schema!, success] }],
            };
        }
    }
}
