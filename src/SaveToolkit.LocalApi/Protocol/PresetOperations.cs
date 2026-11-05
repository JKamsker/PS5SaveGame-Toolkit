using SaveToolkit.Api.Contracts;
using SaveToolkit.Core.Presets;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;

namespace SaveToolkit.LocalApi.Protocol;

/// <summary>Document preset capture, review and apply, shared by every API host.</summary>
internal static class PresetOperations
{
    public static ApiOutcome Capture(DocumentSession document, CapturePresetRequest request, PresetStore store)
    {
        var capability = document.PresetCapability;
        if (document.PresetDocument is not null && capability is null)
            return Rejected("The semantic preset capability is malformed.", "capabilityMalformed");
        var captureRequest = new PresetCaptureRequest(
            request.ActionId,
            request.Parameters ?? [],
            request.ScopeKind,
            string.Equals(request.ScopeKind, PresetScopeKinds.Selected, StringComparison.Ordinal)
                ? request.SelectedRowKeys ?? []
                : null,
            request.AllMatches);
        var references = document.PinnedReferences as ReferenceContext;
        var referenceFields = references is null ? null : new ReferenceFieldService(references);
        if (PresetContractValidation.ValidateCapture(capability, captureRequest, referenceFields) is { } captureError)
            return Rejected(captureError, "presetContract");
        PresetAction? action;
        try { action = document.CapturePreset(captureRequest); }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            return Rejected($"Preset capture failed: {ex.Message}", "presetContract");
        }
        if (action is null)
            return Rejected("The capture adapter returned no action.", "presetContract");
        if (PresetContractValidation.ValidateCapturedAction(capability, captureRequest, action, referenceFields) is { } actionError)
            return Rejected(actionError, "presetContract");
        var compatibility = new PresetCompatibility(
            document.Plugin.Info.Id,
            document.Plugin.Info.Version,
            [document.File.Directory.TitleId],
            capability?.GameBuild,
            capability?.FormatVariant,
            capability?.ReferencePackIdentity,
            true,
            capability?.PresetKind ?? "toolkit.action-parameters",
            capability?.SchemaFingerprint,
            capability?.FileRole,
            capability?.SchemaVersion);
        return ApiOutcome.Ok(store.Create(request.Name, compatibility, [action]).ToDto());
    }

    public static ApiOutcome Preview(DocumentSession document, string presetId, PresetPlanRequest request,
        PresetStore store, CancellationToken cancellationToken)
    {
        var preset = store.Find(presetId) ?? throw new KeyNotFoundException("Preset not found.");
        var compatibility = PresetStore.CheckCompatibility(preset, document);
        if (!compatibility.Compatible) return Rejected(compatibility.Message!, compatibility.Code);
        if (document.PresetDocument is not null)
        {
            if (PresetContractValidation.ValidateChoices(request.Choices) is { } choiceError)
                return Rejected(choiceError, "presetContract");
            var semantic = document.PlanPreset(preset, request.Revision, request.Choices, cancellationToken);
            var semanticDto = new PresetSemanticPreviewDto(preset.ToDto(), semantic);
            if (semantic.Stale) return ApiOutcome.Conflict(semanticDto);
            if (semantic.Plan.Errors.Count > 0) return ApiOutcome.Unprocessable(semanticDto);
            return ApiOutcome.Ok(semanticDto);
        }
        if (preset.Actions.Count != 1)
            return Rejected("This document has no multi-action preset adapter yet.");
        var action = preset.Actions[0];
        if (action.ScopeKind != "document" || action.Instances is { Count: > 0 })
            return Rejected("This preset needs explicit owned-instance resolution from the plugin.");
        if (document.GenericPresetActionError(action.ActionId) is { } actionError)
            return Rejected(actionError, "actionUnsupported");
        var fingerprint = PresetStore.PayloadFingerprint(preset);
        var preview = document.PlanAction(action.ActionId, action.Parameters, new ActionScope(), request.Revision, preset.Id, fingerprint, cancellationToken);
        var dto = new PresetPreviewDto(preset.ToDto(), preview);
        if (preview.Stale) return ApiOutcome.Conflict(dto);
        if (preview.Plan.Errors.Count > 0) return ApiOutcome.Unprocessable(dto);
        return ApiOutcome.Ok(dto);
    }

    public static ApiOutcome Apply(DocumentSession document, string presetId, ApplyPresetRequest request,
        PresetStore store, CancellationToken cancellationToken)
    {
        var preset = store.Find(presetId) ?? throw new KeyNotFoundException("Preset not found.");
        var compatibility = PresetStore.CheckCompatibility(preset, document);
        if (!compatibility.Compatible) return Rejected(compatibility.Message!, compatibility.Code);
        var fingerprint = PresetStore.PayloadFingerprint(preset);
        if (document.PresetDocument is not null)
            return ApiOutcome.ForEdit(document.ApplyPresetPlan(request.PlanId, request.Revision, preset.Id, fingerprint, cancellationToken));
        if (preset.Actions.Count != 1)
            return Rejected("This document has no multi-action preset adapter yet.");
        if (document.GenericPresetActionError(preset.Actions[0].ActionId) is { } actionError)
            return Rejected(actionError, "actionUnsupported");
        return ApiOutcome.ForEdit(document.ApplyActionPlan(request.PlanId, request.Revision, preset.Id, fingerprint, cancellationToken));
    }

    private static ApiOutcome Rejected(string message, string? code = null) => ApiOutcome.Unprocessable(new ErrorDto(message, code));
}
