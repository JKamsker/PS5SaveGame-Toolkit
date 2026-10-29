namespace SaveToolkit.Api.Infrastructure;

internal static class OpenApiRegistration
{
    internal static void AddToolkitOpenApi(this IServiceCollection services) => services.AddOpenApi("v1", options =>
    {
        options.ShouldInclude = description => description.RelativePath?.StartsWith("api/", StringComparison.Ordinal) == true;
        options.AddOperationTransformer<OpenApiOperationMetadata>();
        options.AddSchemaTransformer<OpenApiWireSchemas>();
        options.AddDocumentTransformer<OpenApiPolymorphismSchemas>();
    });

    // Produces metadata is descriptive only: these conventions never change request handling.
    internal static TBuilder WithApiErrors<TBuilder>(this TBuilder builder, params int[] statuses)
        where TBuilder : IEndpointConventionBuilder => builder.WithMetadata(statuses.Select(status =>
            new ProducesResponseTypeMetadata(status, typeof(OpenApiError), ["application/json"])).ToArray());
}

/// <summary>Documentation model for the fields emitted by ApiProblem, including its legacy extensions.</summary>
internal sealed record OpenApiError(string Type, string Title, int Status, string Detail, string Error, string? Code = null);
