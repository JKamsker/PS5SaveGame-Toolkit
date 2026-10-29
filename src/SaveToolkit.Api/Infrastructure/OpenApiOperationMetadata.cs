using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.OpenApi;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>Describes custom binders and response writers without adding runtime binding or validation.</summary>
internal sealed class OpenApiOperationMetadata : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        string path = "/" + context.Description.RelativePath?.TrimEnd('/');
        operation.Parameters ??= [];
        foreach (var parameter in RoutePatternFactory.Parse(path).Parameters)
        {
            if (operation.Parameters.Any(existing => existing.In == ParameterLocation.Path && existing.Name == parameter.Name)) continue;
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = parameter.Name, In = ParameterLocation.Path, Required = true,
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            });
        }

        if (path.Contains("/documents/", StringComparison.Ordinal))
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "If-Match", In = ParameterLocation.Header,
                Description = "Optional strong revision ETag precondition. Malformed syntax returns 400; mismatch returns 412.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            });
        }

        if (context.Description.HttpMethod == "POST" && path is "/api/workspaces" or "/api/reference-packs")
            DescribeUpload(operation, path == "/api/workspaces");
        if (path.EndsWith("/collections", StringComparison.Ordinal) || path.EndsWith("/collections/{collectionId}", StringComparison.Ordinal))
            DescribeCollectionQuery(operation);

        if (operation.Responses?.TryGetValue("200", out var binary) == true && binary.Content is { } content
            && content.Keys.Any(media => media != "application/json"))
        {
            if (path.EndsWith("/assets", StringComparison.Ordinal))
            {
                content.Clear();
                content["*/*"] = new OpenApiMediaType();
            }
            foreach (var media in content.Values)
                media.Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" };
        }

        // ApiProblem retains the result envelope on edit/preview failures. Resolution/transport
        // exceptions can still return a plain problem at the same status, so both are possible.
        if (operation.Responses?.TryGetValue("200", out var success) == true
            && success.Content?.TryGetValue("application/json", out var json) == true
            && (path.EndsWith("/edits", StringComparison.Ordinal) || path.EndsWith("/actions", StringComparison.Ordinal)
                || path.EndsWith("/undo", StringComparison.Ordinal) || path.EndsWith("/redo", StringComparison.Ordinal)
                || path.EndsWith("/actions/plan", StringComparison.Ordinal) || path.EndsWith("/actions/apply", StringComparison.Ordinal)
                || path.EndsWith("/changes/revert", StringComparison.Ordinal)))
        {
            foreach (string status in new[] { "409", "422" })
            {
                var error = operation.Responses[status].Content!["application/json"];
                error.Schema = new OpenApiSchema { AnyOf = [error.Schema!, new OpenApiSchema { AllOf = [error.Schema!, json.Schema!] }] };
            }
        }
        return Task.CompletedTask;
    }

    private static void DescribeUpload(OpenApiOperation operation, bool workspace)
    {
        var properties = new Dictionary<string, IOpenApiSchema>
        {
            ["files"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Array, MinItems = 1, MaxItems = workspace ? null : 1,
                Items = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" },
            },
        };
        if (workspace) properties["name"] = new OpenApiSchema { Type = JsonSchemaType.String };
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description = "File-part names are unrestricted; 'files' is illustrative. " +
                (workspace ? "One or more decrypted ZIPs or loose files; optional name." : "Exactly one reference-pack ZIP."),
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.Object, Properties = properties } },
            },
        };
    }

    private static void DescribeCollectionQuery(OpenApiOperation operation)
    {
        foreach (string name in new[] { "offset", "limit", "q", "text", "filters", "sort", "descending", "includeEmpty", "cursor", "revision" })
        {
            operation.Parameters!.Add(new OpenApiParameter
            {
                Name = name, In = ParameterLocation.Query,
                Description = name == "filters" ? "JSON object of string values; filter.<column> query parameters override individual keys." : null,
                Schema = new OpenApiSchema
                {
                    Type = name is "offset" or "limit" or "revision" ? JsonSchemaType.Integer
                        : name is "descending" or "includeEmpty" ? JsonSchemaType.Boolean : JsonSchemaType.String,
                },
            });
        }
    }
}
