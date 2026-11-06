using SaveToolkit.Api.Contracts;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.Presets;
using SaveToolkit.LocalApi.Http;
using SaveToolkit.LocalApi.Protocol;
using SaveToolkit.LocalApi.Routing;
using static SaveToolkit.LocalApi.Endpoints.EndpointFilters;

namespace SaveToolkit.LocalApi.Endpoints;

/// <summary>Preset library and document preset routes (ASP.NET: PresetEndpoints, PresetTransferEndpoints).</summary>
internal static class PresetRoutes
{
    private const string DocumentPresets = DocumentRoutes.Document + "/presets";

    public static void Map(RouteTable routes)
    {
        routes.Map("GET", "/api/presets", c => c.RunAsync([], null, () => Sync(() => c.Services.Presets.List().Select(p => p.ToDto()))));
        routes.Map("POST", "/api/presets/reload", c => c.RunAsync([], null, () => Sync(() => c.Services.Presets.Reload().Select(p => p.ToDto()))));
        routes.Map("POST", "/api/presets", c =>
        {
            var request = c.Body<CreatePresetRequest>("CreatePresetRequest", "request")!;
            return c.RunAsync([request], null, () => Sync(() =>
                ApiOutcome.Ok(c.Services.Presets.Create(request.Name, request.Compatibility, request.Actions).ToDto())));
        }, presetBodyLimit: PresetRequestBodyLimits.CreateBytes, json: true);
        routes.Map("PATCH", "/api/presets/{presetId}", c =>
        {
            var presetId = c.Route("presetId");
            var request = c.Body<RenamePresetRequest>("RenamePresetRequest", "request")!;
            return c.RunAsync([request], null, () => Sync(() => ApiOutcome.Ok(c.Services.Presets.Rename(presetId, request.Name).ToDto())));
        }, presetBodyLimit: PresetRequestBodyLimits.RenameBytes, json: true);
        routes.Map("DELETE", "/api/presets/{presetId}", c => c.RunAsync([], null, () => Sync(() =>
            c.Services.Presets.Delete(c.Route("presetId")) ? new EmptyResult(204) : new EmptyResult(404))));

        routes.Map("POST", DocumentPresets + "/{presetId}/preview", c =>
        {
            var d = c.Document();
            var request = c.Body<PresetPlanRequest>("PresetPlanRequest", "request")!;
            var presetId = c.Route("presetId");
            return c.RunAsync([request], d, () => Sync(() => PresetOperations.Preview(d.Value, presetId, request, c.Services.Presets, c.Aborted)));
        }, presetBodyLimit: PresetRequestBodyLimits.PreviewBytes, json: true);
        routes.Map("POST", DocumentPresets + "/{presetId}/apply", c =>
        {
            var d = c.Document();
            var request = c.Body<ApplyPresetRequest>("ApplyPresetRequest", "request")!;
            var presetId = c.Route("presetId");
            return c.RunAsync([request], d, () => Sync(() => PresetOperations.Apply(d.Value, presetId, request, c.Services.Presets, c.Aborted)));
        }, presetBodyLimit: PresetRequestBodyLimits.ApplyBytes, json: true);
        routes.Map("GET", DocumentPresets, c => DocumentRoutes.Get(c, d => c.Services.Presets.List().Select(p => p.ToDocumentDto(d.Value))));
        routes.Map("POST", DocumentPresets + "/capture", c =>
        {
            var d = c.Document();
            var request = c.Body<CapturePresetRequest>("CapturePresetRequest", "request")!;
            return c.RunAsync([request], d, () => Sync(() => PresetOperations.Capture(d.Value, request, c.Services.Presets)));
        }, presetBodyLimit: PresetRequestBodyLimits.CaptureBytes, json: true);

        MapTransfer(routes);
    }

    private static void MapTransfer(RouteTable routes)
    {
        const string Transfer = "/api/presets/transfer";
        routes.Map("POST", Transfer + "/export", c => c.RunAsync([], null, async () =>
        {
            var selection = await ReadControlAsync<PresetExportRequest>(c);
            return new BytesResult(200, c.Services.Transfers.Export(selection), "application/json", PresetTransferControl.ExportFileName);
        }), json: true);
        routes.Map("POST", Transfer + "/preview", c => c.RunAsync([], null, async () =>
        {
            PresetTransferControl.CheckDeclaredLength(c.Http.Body.Length, PresetPackageCodec.MaxBytes);
            var bytes = await PresetTransferControl.ReadPackageAsync(UploadReader.OpenRead(c.Http.Body), c.Http.Body.Length, c.Aborted);
            return ApiOutcome.Ok(c.Services.Transfers.Preview(bytes));
        }), json: true);
        routes.Map("POST", Transfer + "/resolve", c => c.RunAsync([], null, async () =>
            ApiOutcome.Ok(c.Services.Transfers.Resolve(await ReadControlAsync<PresetImportResolution>(c)))), json: true);
        routes.Map("POST", Transfer + "/commit", c => c.RunAsync([], null, async () =>
            ApiOutcome.Ok(c.Services.Transfers.Commit(await ReadControlAsync<PresetImportCommit>(c)))), json: true);
        routes.Map("POST", DocumentPresets + "/transfer/compatibility", c =>
        {
            var d = c.Document();
            return c.RunAsync([], d, async () =>
            {
                var token = await ReadControlAsync<PresetImportCommit>(c);
                return ApiOutcome.Ok(c.Services.Transfers.GetPreviewPresets(token.Token).Select((preset, index) =>
                    new PresetImportCompatibilityDto(index, preset.ToDocumentDto(d.Value).Compatibility)));
            });
        }, json: true);
        routes.Map("DELETE", Transfer + "/previews/{token}", c => c.RunAsync([], null, () => Sync(() =>
        {
            c.Services.Transfers.Cancel(c.Route("token"));
            return new EmptyResult(204);
        })));
    }

    private static async Task<T> ReadControlAsync<T>(EndpointContext c)
    {
        PresetTransferControl.CheckDeclaredLength(c.Http.Body.Length, PresetTransferControl.ControlRequestLimit);
        return await PresetTransferControl.ReadControlAsync<T>(UploadReader.OpenRead(c.Http.Body), c.Http.Body.Length, c.Aborted);
    }
}
