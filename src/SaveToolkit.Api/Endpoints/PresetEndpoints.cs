using SaveToolkit.Api.Contracts;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.Presets;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace SaveToolkit.Api.Endpoints;

internal static class PresetEndpoints
{
    public static void MapPresetApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter<ApiContractFilter>()
            .WithApiErrors(Status400BadRequest, Status404NotFound, Status409Conflict, Status422UnprocessableEntity, Status503ServiceUnavailable);
        api.MapGet("/presets", (PresetStore store) => store.List().Select(p => p.ToDto()));
        api.MapPost("/presets/reload", (PresetStore store) => store.Reload().Select(p => p.ToDto()));
        api.MapPost("/presets", (CreatePresetRequest request, PresetStore store) =>
            Results.Ok(store.Create(request.Name, request.Compatibility, request.Actions).ToDto()));
        api.MapPatch("/presets/{presetId}", (string presetId, RenamePresetRequest request, PresetStore store) =>
            Results.Ok(store.Rename(presetId, request.Name).ToDto()));
        api.MapDelete("/presets/{presetId}", (string presetId, PresetStore store) =>
            store.Delete(presetId) ? Results.NoContent() : Results.NotFound());

        var docs = api.MapGroup("/workspaces/{workspaceId}/documents/{documentId}/presets/{presetId}")
            .AddEndpointFilter<DocumentRevisionFilter>()
            .WithApiErrors(Status412PreconditionFailed);
        docs.MapPost("/preview", Preview);
        docs.MapPost("/apply", Apply);
        var capture = api.MapGroup("/workspaces/{workspaceId}/documents/{documentId}/presets")
            .AddEndpointFilter<DocumentRevisionFilter>()
            .WithApiErrors(Status412PreconditionFailed);
        capture.MapGet("", (DocumentRef document, PresetStore store) =>
            store.List().Select(p => p.ToDocumentDto(document.Value)));
        capture.MapPost("/capture", Capture);
    }

    private static IResult Capture(DocumentRef document, CapturePresetRequest request, PresetStore store)
    {
        var capability = document.Value.PresetCapability;
        if (document.Value.PresetDocument is not null && capability is null)
            return Results.UnprocessableEntity(new ErrorDto("The semantic preset capability is malformed.", "capabilityMalformed"));
        var captureRequest = new SaveToolkit.Sdk.PresetCaptureRequest(
            request.ActionId,
            request.Parameters ?? [],
            request.ScopeKind,
            string.Equals(request.ScopeKind, PresetScopeKinds.Selected, StringComparison.Ordinal)
                ? request.SelectedRowKeys ?? []
                : null,
            request.AllMatches);
        var references = document.Value.PinnedReferences as ReferenceContext;
        var referenceFields = references is null ? null : new ReferenceFieldService(references);
        if (PresetContractValidation.ValidateCapture(capability, captureRequest, referenceFields) is { } captureError)
            return Results.UnprocessableEntity(new ErrorDto(captureError, "presetContract"));
        PresetAction? action;
        try { action = document.Value.CapturePreset(captureRequest); }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            return Results.UnprocessableEntity(new ErrorDto($"Preset capture failed: {ex.Message}", "presetContract"));
        }
        if (action is null)
            return Results.UnprocessableEntity(new ErrorDto("The capture adapter returned no action.", "presetContract"));
        if (PresetContractValidation.ValidateCapturedAction(capability, captureRequest, action, referenceFields) is { } actionError)
            return Results.UnprocessableEntity(new ErrorDto(actionError, "presetContract"));
        var compatibility = new PresetCompatibility(
            document.Value.Plugin.Info.Id,
            document.Value.Plugin.Info.Version,
            [document.Value.File.Directory.TitleId],
            capability?.GameBuild,
            capability?.FormatVariant,
            capability?.ReferencePackIdentity,
            true,
            capability?.PresetKind ?? "toolkit.action-parameters",
            capability?.SchemaFingerprint,
            capability?.FileRole,
            capability?.SchemaVersion);
        return Results.Ok(store.Create(request.Name, compatibility, [action]).ToDto());
    }

    private static IResult Preview(DocumentRef document, string presetId, PresetPlanRequest request,
        PresetStore store, CancellationToken cancellationToken)
    {
        var preset = store.Find(presetId) ?? throw new KeyNotFoundException("Preset not found.");
        var compatibility = PresetStore.CheckCompatibility(preset, document.Value);
        if (!compatibility.Compatible) return Results.UnprocessableEntity(new ErrorDto(compatibility.Message!, compatibility.Code));
        if (document.Value.PresetDocument is not null)
        {
            if (PresetContractValidation.ValidateChoices(request.Choices) is { } choiceError)
                return Results.UnprocessableEntity(new ErrorDto(choiceError, "presetContract"));
            var semantic = document.Value.PlanPreset(preset, request.Revision, request.Choices, cancellationToken);
            if (semantic.Stale) return Results.Conflict(new PresetSemanticPreviewDto(preset.ToDto(), semantic));
            if (semantic.Plan.Errors.Count > 0) return Results.UnprocessableEntity(new PresetSemanticPreviewDto(preset.ToDto(), semantic));
            return Results.Ok(new PresetSemanticPreviewDto(preset.ToDto(), semantic));
        }
        if (preset.Actions.Count != 1)
            return Results.UnprocessableEntity(new ErrorDto("This document has no multi-action preset adapter yet."));
        var action = preset.Actions[0];
        if (action.ScopeKind != "document" || action.Instances is { Count: > 0 })
            return Results.UnprocessableEntity(new ErrorDto("This preset needs explicit owned-instance resolution from the plugin."));
        var fingerprint = PresetStore.PayloadFingerprint(preset);
        var preview = document.Value.PlanAction(action.ActionId, action.Parameters, new ActionScope(), request.Revision, preset.Id, fingerprint, cancellationToken);
        var dto = new PresetPreviewDto(preset.ToDto(), preview);
        if (preview.Stale) return Results.Conflict(dto);
        if (preview.Plan.Errors.Count > 0) return Results.UnprocessableEntity(dto);
        return Results.Ok(dto);
    }

    private static IResult Apply(DocumentRef document, string presetId, ApplyPresetRequest request,
        PresetStore store, CancellationToken cancellationToken)
    {
        var preset = store.Find(presetId) ?? throw new KeyNotFoundException("Preset not found.");
        var compatibility = PresetStore.CheckCompatibility(preset, document.Value);
        if (!compatibility.Compatible) return Results.UnprocessableEntity(new ErrorDto(compatibility.Message!, compatibility.Code));
        var fingerprint = PresetStore.PayloadFingerprint(preset);
        if (document.Value.PresetDocument is not null)
            return SessionEditResponse.Create(document.Value.ApplyPresetPlan(request.PlanId, request.Revision, preset.Id, fingerprint, cancellationToken));
        if (preset.Actions.Count != 1)
            return Results.UnprocessableEntity(new ErrorDto("This document has no multi-action preset adapter yet."));
        var result = document.Value.ApplyActionPlan(request.PlanId, request.Revision, preset.Id, fingerprint, cancellationToken);
        return SessionEditResponse.Create(result);
    }
}
