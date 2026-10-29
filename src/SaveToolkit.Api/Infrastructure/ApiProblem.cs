using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Core;
using SaveToolkit.Core.Sessions;

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
    {
        var fields = body is null ? new Dictionary<string, object?>() : JsonSerializer
            .SerializeToElement(body, body.GetType(), ToolkitJson.Options).EnumerateObject()
            .ToDictionary(property => property.Name, property => (object?)property.Value, StringComparer.Ordinal);
        string title = ReasonPhrases.GetReasonPhrase(status);
        string detail = body switch
        {
            ErrorDto error => error.Error,
            SessionEditResult result => string.Join(" ", result.Errors.Select(error => error.Message)),
            ActionPreview preview => string.Join(" ", preview.Plan.Errors.Select(error => error.Message)),
            _ => title,
        };
        if (string.IsNullOrWhiteSpace(detail)) detail = title;
        fields["type"] = "about:blank";
        fields["title"] = title;
        fields["status"] = status;
        fields["detail"] = detail;
        fields["error"] = detail;
        return fields;
    }

    internal static object? Normalize(object? result)
    {
        if (result is IValueHttpResult { Value: Dictionary<string, object?> fields } && fields.ContainsKey("type"))
            return result;
        return result is IStatusCodeHttpResult { StatusCode: >= 400 } error
            ? Create(error.StatusCode.Value, (result as IValueHttpResult)?.Value) : result;
    }
}
