using SaveToolkit.Api.Contracts;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.Presets;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace SaveToolkit.Api.Endpoints;

internal static class PresetEndpoints
{
    public static void MapPresetApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter<ApiContractFilter>()
            .WithApiErrors(Status400BadRequest, Status404NotFound, Status409Conflict, Status422UnprocessableEntity);
        api.MapGet("/presets", (PresetStore store) => store.List().Select(p => p.ToDto()));
        api.MapPost("/presets", (CreatePresetRequest request, PresetStore store) =>
            Results.Ok(store.Create(request.Name, request.Compatibility, request.Actions).ToDto()));
        api.MapPatch("/presets/{presetId}", (string presetId, RenamePresetRequest request, PresetStore store) =>
            Results.Ok(store.Rename(presetId, request.Name).ToDto()));
        api.MapDelete("/presets/{presetId}", (string presetId, PresetStore store) =>
            store.Delete(presetId) ? Results.NoContent() : Results.NotFound());

        var docs = api.MapGroup("/workspaces/{workspaceId}/documents/{documentId}/presets/{presetId}")
            .AddEndpointFilter<DocumentRevisionFilter>();
        docs.MapPost("/preview", Preview);
        docs.MapPost("/apply", Apply);
        var capture = api.MapGroup("/workspaces/{workspaceId}/documents/{documentId}/presets")
            .AddEndpointFilter<DocumentRevisionFilter>();
        capture.MapPost("/capture", Capture);
    }

    private static IResult Capture(DocumentRef document, CapturePresetRequest request, PresetStore store)
    {
        var capability = document.Value.PresetDocument?.PresetCapability;
        var action = document.Value.CapturePreset(new SaveToolkit.Sdk.PresetCaptureRequest(
            request.ActionId,
            request.Parameters ?? [],
            request.ScopeKind,
            request.SelectedRowKeys ?? [],
            request.AllMatches));
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
            capability?.FileRole);
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
            var semantic = document.Value.PlanPreset(preset, request.Revision, request.Choices, cancellationToken);
            if (semantic.Stale) return Results.Conflict(semantic);
            if (semantic.Plan.Errors.Count > 0) return Results.UnprocessableEntity(new PresetSemanticPreviewDto(preset.ToDto(), semantic));
            return Results.Ok(new PresetSemanticPreviewDto(preset.ToDto(), semantic));
        }
        if (preset.Actions.Count != 1)
            return Results.UnprocessableEntity(new ErrorDto("This document has no multi-action preset adapter yet."));
        var action = preset.Actions[0];
        if (action.ScopeKind != "document" || action.Instances is { Count: > 0 })
            return Results.UnprocessableEntity(new ErrorDto("This preset needs explicit owned-instance resolution from the plugin."));
        var preview = document.Value.PlanAction(action.ActionId, action.Parameters, new ActionScope(), request.Revision, cancellationToken);
        if (preview.Stale) return Results.Conflict(preview);
        if (preview.Plan.Errors.Count > 0) return Results.UnprocessableEntity(preview);
        return Results.Ok(new PresetPreviewDto(preset.ToDto(), preview));
    }

    private static IResult Apply(DocumentRef document, string presetId, ApplyPresetRequest request,
        PresetStore store, CancellationToken cancellationToken)
    {
        var preset = store.Find(presetId) ?? throw new KeyNotFoundException("Preset not found.");
        var compatibility = PresetStore.CheckCompatibility(preset, document.Value);
        if (!compatibility.Compatible) return Results.UnprocessableEntity(new ErrorDto(compatibility.Message!, compatibility.Code));
        if (document.Value.PresetDocument is not null)
            return SessionEditResponse.Create(document.Value.ApplyPresetPlan(request.PlanId, request.Revision, cancellationToken));
        if (preset.Actions.Count != 1)
            return Results.UnprocessableEntity(new ErrorDto("This document has no multi-action preset adapter yet."));
        var result = document.Value.ApplyActionPlan(request.PlanId, request.Revision, cancellationToken);
        return SessionEditResponse.Create(result);
    }
}
