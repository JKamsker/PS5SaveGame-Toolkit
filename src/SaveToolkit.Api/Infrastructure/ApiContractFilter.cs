namespace SaveToolkit.Api.Infrastructure;

/// <summary>Applies request validation and additive error metadata to every mapped API endpoint.</summary>
internal sealed class ApiContractFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (object? argument in context.Arguments)
        {
            if (ApiRequestValidation.Error(argument) is { } error)
                return ApiProblem.Create(StatusCodes.Status400BadRequest, new Contracts.ErrorDto(error));
        }
        return ApiProblem.Normalize(await next(context));
    }
}
