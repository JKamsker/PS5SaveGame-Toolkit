using SaveToolkit.Api.Contracts;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.Presets;
using SaveToolkit.Core.Sessions;
using SaveToolkit.LocalApi.Protocol;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace SaveToolkit.Api.Endpoints;

internal static class PresetEndpoints
{
    public static void MapPresetApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter<ApiContractFilter>()
            .WithApiErrors(Status400BadRequest, Status404NotFound, Status409Conflict, Status422UnprocessableEntity, Status413PayloadTooLarge, Status503ServiceUnavailable);
        api.MapGet("/presets", (PresetStore store) => store.List().Select(p => p.ToDto()));
        api.MapPost("/presets/reload", (PresetStore store) => store.Reload().Select(p => p.ToDto()));
        api.MapPost("/presets", (CreatePresetRequest request, PresetStore store) =>
            Results.Ok(store.Create(request.Name, request.Compatibility, request.Actions).ToDto()))
            .WithMetadata(new PresetRequestBodyLimitMetadata(PresetRequestBodyLimits.CreateBytes));
        api.MapPatch("/presets/{presetId}", (string presetId, RenamePresetRequest request, PresetStore store) =>
            Results.Ok(store.Rename(presetId, request.Name).ToDto()))
            .WithMetadata(new PresetRequestBodyLimitMetadata(PresetRequestBodyLimits.RenameBytes));
        api.MapDelete("/presets/{presetId}", (string presetId, PresetStore store) =>
            store.Delete(presetId) ? Results.NoContent() : Results.NotFound());

        var docs = api.MapGroup("/workspaces/{workspaceId}/documents/{documentId}/presets/{presetId}")
            .AddEndpointFilter<DocumentRevisionFilter>()
            .WithApiErrors(Status412PreconditionFailed);
        docs.MapPost("/preview", Preview)
            .Produces<PresetPreviewDto>()
            .WithMetadata(new PresetRequestBodyLimitMetadata(PresetRequestBodyLimits.PreviewBytes));
        docs.MapPost("/apply", Apply)
            .Produces<SessionEditResult>()
            .WithMetadata(new PresetRequestBodyLimitMetadata(PresetRequestBodyLimits.ApplyBytes));
        var capture = api.MapGroup("/workspaces/{workspaceId}/documents/{documentId}/presets")
            .AddEndpointFilter<DocumentRevisionFilter>()
            .WithApiErrors(Status412PreconditionFailed);
        capture.MapGet("", (DocumentRef document, PresetStore store) =>
            store.List().Select(p => p.ToDocumentDto(document.Value)));
        capture.MapPost("/capture", Capture)
            .Produces<PresetDto>()
            .WithMetadata(new PresetRequestBodyLimitMetadata(PresetRequestBodyLimits.CaptureBytes));
    }

    private static IResult Capture(DocumentRef document, CapturePresetRequest request, PresetStore store) =>
        PresetOperations.Capture(document.Value, request, store).ToResult();

    private static IResult Preview(DocumentRef document, string presetId, PresetPlanRequest request,
        PresetStore store, CancellationToken cancellationToken) =>
        PresetOperations.Preview(document.Value, presetId, request, store, cancellationToken).ToResult();

    private static IResult Apply(DocumentRef document, string presetId, ApplyPresetRequest request,
        PresetStore store, CancellationToken cancellationToken) =>
        PresetOperations.Apply(document.Value, presetId, request, store, cancellationToken).ToResult();
}
