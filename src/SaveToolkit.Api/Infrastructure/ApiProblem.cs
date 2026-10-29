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
        var fields = BodyFields(body);
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

    private static Dictionary<string, object?> BodyFields(object? body)
    {
        // Keep nested payloads typed: errors can contain large preview/change lists. A JSON
        // DOM here would traverse and copy them before the response serializes them again.
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        switch (body)
        {
            case null:
                break;
            case ErrorDto error:
                fields["error"] = error.Error;
                if (error.Code is not null) fields["code"] = error.Code;
                break;
            case SessionEditResult edit:
                fields["ok"] = edit.Ok;
                fields["errors"] = edit.Errors;
                fields["changes"] = edit.Changes;
                fields["warnings"] = edit.Warnings;
                if (edit.Message is not null) fields["message"] = edit.Message;
                fields["structureChanged"] = edit.StructureChanged;
                fields["stale"] = edit.Stale;
                if (edit.Code is not null) fields["code"] = edit.Code;
                fields["state"] = edit.State;
                break;
            case ActionPreview preview:
                if (preview.PlanId is not null) fields["planId"] = preview.PlanId;
                fields["revision"] = preview.Revision;
                fields["scope"] = preview.Scope;
                fields["plan"] = preview.Plan;
                fields["stale"] = preview.Stale;
                if (preview.Code is not null) fields["code"] = preview.Code;
                break;
            default:
                foreach (var property in JsonSerializer.SerializeToElement(body, body.GetType(), ToolkitJson.Options).EnumerateObject())
                    fields[property.Name] = property.Value;
                break;
        }
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
