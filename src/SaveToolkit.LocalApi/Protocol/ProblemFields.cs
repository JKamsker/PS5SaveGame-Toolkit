using System.Text.Json;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Core;
using SaveToolkit.Core.Sessions;

namespace SaveToolkit.LocalApi.Protocol;

/// <summary>ProblemDetails metadata with the existing client-facing error/result fields retained.</summary>
internal static class ProblemFields
{
    /// <param name="title">The standard reason phrase for <paramref name="status"/> (empty when none exists).</param>
    public static Dictionary<string, object?> Create(int status, string title, object? body)
    {
        var fields = BodyFields(body);
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

    /// <summary>True for a value that is already a problem envelope built by <see cref="Create"/>.</summary>
    public static bool IsProblem(object? value) => value is Dictionary<string, object?> fields && fields.ContainsKey("type");

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
}
