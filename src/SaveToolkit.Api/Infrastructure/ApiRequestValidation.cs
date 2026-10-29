using System.Text.Json;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Api.Endpoints;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>Validates transport shape; field specs and plugin rules remain the session's responsibility.</summary>
internal static class ApiRequestValidation
{
    internal static string? Error(object? request) => request switch
    {
        EditRequest r when r.Edits is null => "Edits is required and must be an array.",
        EditRequest r when r.Edits.Any(edit => edit is null || Missing(edit.ValueId)
            || edit.Value.ValueKind == JsonValueKind.Undefined) => "Every edit requires a valueId and a value.",
        EditRequest { Revision: < 0 } => "Revision cannot be negative.",
        ActionRequest r when Missing(r.ActionId) => "ActionId is required.",
        ActionRequest { Revision: < 0 } => "Revision cannot be negative.",
        PlanActionRequest r when Missing(r.ActionId) => "ActionId is required.",
        PlanActionRequest { Revision: < 0 } => "Revision cannot be negative.",
        ApplyActionPlanRequest r when Missing(r.PlanId) => "PlanId is required.",
        ApplyActionPlanRequest { Revision: < 0 } => "Revision cannot be negative.",
        RevertChangeRequest r when Missing(r.ValueId) => "ValueId is required.",
        RevertChangeRequest { Revision: < 0 } => "Revision cannot be negative.",
        HistoryRequest { Revision: < 0 } => "Revision cannot be negative.",
        OpenGroupRequest r when Missing(r.ProposalToken) => "ProposalToken is required.",
        ExportRequest r when !Enum.IsDefined(r.Scope) => "Unknown export scope.",
        _ => null,
    };

    private static bool Missing(string? value) => string.IsNullOrWhiteSpace(value);
}
