using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Net.Http.Headers;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Api.Endpoints;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>Serializes HTTP document operations so history/header preconditions and execution cannot interleave.</summary>
internal sealed class DocumentRevisionFilter : IEndpointFilter
{
    private static readonly ConditionalWeakTable<DocumentSession, SemaphoreSlim> Gates = new();

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var document = context.Arguments.OfType<DocumentRef>().SingleOrDefault()?.Value;
        if (document is null) return await next(context);
        var gate = Gates.GetValue(document, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(context.HttpContext.RequestAborted);
        try
        {
            var http = context.HttpContext;
            if (http.Request.Headers.TryGetValue("If-Match", out var raw))
            {
                if (!EntityTagHeaderValue.TryParseStrictList(raw.Select(value => value ?? string.Empty).ToArray(), out var tags) || tags.Count == 0
                    || tags.Count > 1 && tags.Any(tag => tag == EntityTagHeaderValue.Any))
                    return ApiProblem.Create(StatusCodes.Status400BadRequest, new ErrorDto("If-Match must contain valid entity tags or *."));
                string currentTag = Tag(document.State.Revision);
                if (!tags.Any(tag => tag == EntityTagHeaderValue.Any || !tag.IsWeak && tag.Tag == currentTag))
                    return ApiProblem.Create(StatusCodes.Status412PreconditionFailed,
                        new ErrorDto(new StaleViewException().Message, StaleViewException.Code));
            }

            if (context.Arguments.OfType<HistoryRequest>().SingleOrDefault()?.Revision is { } expected)
            {
                var state = document.State;
                if (expected != state.Revision)
                    return Results.Conflict(new SessionEditResult
                    {
                        Stale = true, State = state,
                        Errors = [new FieldError(string.Empty, new StaleViewException().Message)],
                    });
            }

            object? result = await next(context);
            var value = result is IValueHttpResult response ? response.Value : result;
            long? revision = value switch
            {
                DocumentDto dto => dto.State.Revision,
                SessionEditResult edit => edit.State.Revision,
                ActionPreview preview => preview.Revision,
                _ => null,
            };
            if (revision is { } current) http.Response.Headers.ETag = Tag(current);
            return result;
        }
        finally { gate.Release(); }
    }

    private static string Tag(long revision) => $"\"{revision.ToString(CultureInfo.InvariantCulture)}\"";
}
