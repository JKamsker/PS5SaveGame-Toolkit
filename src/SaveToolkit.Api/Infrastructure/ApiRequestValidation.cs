using System.Text.Json;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Api.Endpoints;
using SaveToolkit.Core.Presets;
using SaveToolkit.Sdk;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>Validates transport shape; field specs and plugin rules remain the session's responsibility.</summary>
internal static class ApiRequestValidation
{
    internal static string? Error(object? request) => request switch
    {
        ReferenceRecordsRequest r when r.Pairs is null => "Pairs is required and must be an array.",
        ReferenceRecordsRequest r when r.Pairs.Count > ReferenceRecordsRequest.MaxPairs =>
            throw new BadHttpRequestException($"At most {ReferenceRecordsRequest.MaxPairs} reference pairs are allowed.",
                StatusCodes.Status413PayloadTooLarge),
        ReferenceRecordsRequest r when r.Pairs.Any(pair => pair is null || Missing(pair.FieldId)
            || pair.RecordKey is null || pair.ActionId is not null && Missing(pair.ActionId)) =>
            "Every reference pair requires a fieldId, a string recordKey, and a nonempty actionId when supplied.",
        ReferenceRecordsRequest { Revision: < 0 } => "Revision cannot be negative.",
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
        CreatePresetRequest r when Missing(r.Name) => "Preset name is required.",
        CreatePresetRequest r when r.Name.Trim().Length > 100 => "Preset name cannot exceed 100 characters.",
        CreatePresetRequest r when r.Compatibility is null || Missing(r.Compatibility.PluginId)
            || Missing(r.Compatibility.PluginVersion) || r.Compatibility.TitleIds is null || r.Compatibility.TitleIds.Count == 0
            || r.Compatibility.TitleIds.Any(Missing) => "Preset compatibility requires plugin, version and at least one title id.",
        CreatePresetRequest r when r.Actions is null || r.Actions.Count is < 1 or > 32 => "Preset actions must contain between 1 and 32 actions.",
        CreatePresetRequest r when r.Actions.Any(action => action is null || Missing(action.ActionId) || action.Parameters is null || action.Parameters.Count > 128 || action.Parameters.Keys.Any(key => Missing(key) || key.Length > PresetContractValidation.MaxKeyLength) || Missing(action.ScopeKind) || !ValidScope(action.ScopeKind)) => "Every preset action requires an id, at most 128 bounded parameters and a supported scope.",
        CreatePresetRequest r when JsonSerializer.SerializeToUtf8Bytes(r.Actions).Length > PresetContractValidation.MaxRequestBytes => "Preset actions exceed the bounded request size.",
        RenamePresetRequest r when Missing(r.Name) => "Preset name is required.",
        RenamePresetRequest r when r.Name.Trim().Length > 100 => "Preset name cannot exceed 100 characters.",
        CapturePresetRequest r when Missing(r.Name) || Missing(r.ActionId) => "Preset name and action id are required.",
        CapturePresetRequest r when r.Name.Trim().Length > 100 => "Preset name cannot exceed 100 characters.",
        CapturePresetRequest r when !ValidScope(r.ScopeKind) => "Preset capture requires a supported scope.",
        CapturePresetRequest r when r.SelectedRowKeys is { Count: > 5000 } => "Preset capture selection cannot contain more than 5000 rows.",
        CapturePresetRequest r when r.SelectedRowKeys is { } keys && (keys.Any(string.IsNullOrWhiteSpace) || keys.Any(key => key.Length > PresetContractValidation.MaxKeyLength)
            || keys.Distinct(StringComparer.Ordinal).Count() != keys.Count) => "Preset capture selection must contain distinct bounded row keys.",
        CapturePresetRequest r when r.Parameters is { Count: > 128 } => "Preset capture parameters cannot contain more than 128 entries.",
        CapturePresetRequest r when r.Parameters is { } parameters && parameters.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > PresetContractValidation.MaxKeyLength) => "Preset capture parameter keys are invalid.",
        CapturePresetRequest r when r.Parameters is { } parameters && JsonSerializer.SerializeToUtf8Bytes(parameters).Length > PresetContractValidation.MaxRequestBytes => "Preset capture parameters exceed the bounded request size.",
        PresetPlanRequest { Revision: < 0 } => "Revision cannot be negative.",
        PresetPlanRequest r when r.Choices is { Count: > 128 } => "Preset choices cannot contain more than 128 entries.",
        PresetPlanRequest r when r.Choices is { } choices && choices.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > PresetContractValidation.MaxKeyLength
            || string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > PresetContractValidation.MaxKeyLength) => "Preset choice keys and values are invalid.",
        PresetPlanRequest r when r.Choices is { } choices && JsonSerializer.SerializeToUtf8Bytes(choices).Length > PresetContractValidation.MaxRequestBytes => "Preset choices exceed the bounded request size.",
        ApplyPresetRequest r when Missing(r.PlanId) => "PlanId is required.",
        ApplyPresetRequest { Revision: < 0 } => "Revision cannot be negative.",
        RevertChangeRequest r when Missing(r.ValueId) => "ValueId is required.",
        RevertChangeRequest { Revision: < 0 } => "Revision cannot be negative.",
        HistoryRequest { Revision: < 0 } => "Revision cannot be negative.",
        OpenGroupRequest r when Missing(r.ProposalToken) => "ProposalToken is required.",
        ExportRequest r when !Enum.IsDefined(r.Scope) => "Unknown export scope.",
        _ => null,
    };

    private static bool Missing(string? value) => string.IsNullOrWhiteSpace(value);

    private static bool ValidScope(string? scope) => scope is PresetScopeKinds.Document or PresetScopeKinds.AllEligible
        or PresetScopeKinds.AllCopies or PresetScopeKinds.Selected;
}
