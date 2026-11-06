using Microsoft.AspNetCore.WebUtilities;
using SaveToolkit.Core;
using SaveToolkit.LocalApi.Protocol;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>ProblemDetails metadata with the existing client-facing error/result fields retained.</summary>
internal static class ApiProblem
{
    internal static IResult Create(int status, object? body = null)
        => Results.Json(Fields(status, body), ToolkitJson.Options, contentType: "application/json", statusCode: status);

    internal static Task WriteAsync(HttpContext context, int status, object body, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(Fields(status, body), ToolkitJson.Options, cancellationToken);
    }

    private static Dictionary<string, object?> Fields(int status, object? body)
        => ProblemFields.Create(status, ReasonPhrases.GetReasonPhrase(status), body);

    internal static object? Normalize(object? result)
    {
        if (result is IValueHttpResult { Value: var value } && ProblemFields.IsProblem(value))
            return result;
        return result is IStatusCodeHttpResult { StatusCode: >= 400 } error
            ? Create(error.StatusCode.Value, (result as IValueHttpResult)?.Value) : result;
    }
}
