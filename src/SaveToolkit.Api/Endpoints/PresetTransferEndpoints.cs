using Microsoft.AspNetCore.Http.Features;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.Presets;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace SaveToolkit.Api.Endpoints;

internal static class PresetTransferEndpoints
{
    private const int ControlRequestLimit = 32 * 1024;

    public static void MapPresetTransferApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/presets/transfer").AddEndpointFilter<ApiContractFilter>()
            .WithApiErrors(Status400BadRequest, Status409Conflict, Status413PayloadTooLarge,
                Status422UnprocessableEntity, Status503ServiceUnavailable);
        api.MapPost("/export", Export).Accepts<PresetExportRequest>("application/json")
            .Produces(Status200OK, contentType: "application/json");
        api.MapPost("/preview", Preview).Accepts<PresetPackage>("application/json")
            .Produces<PresetImportPreview>();
        api.MapPost("/resolve", Resolve).Accepts<PresetImportResolution>("application/json")
            .Produces<PresetImportPreview>();
        api.MapPost("/commit", Commit).Accepts<PresetImportCommit>("application/json")
            .Produces<PresetImportResult>();
        api.MapDelete("/previews/{token}", (string token, PresetTransferService service) =>
        {
            service.Cancel(token);
            return Results.NoContent();
        });
    }

    private static async Task<IResult> Preview(HttpRequest request, PresetTransferService service, CancellationToken ct)
    {
        LimitBody(request, PresetPackageCodec.MaxBytes);
        var bytes = await PresetPackageCodec.ReadBoundedAsync(request.Body, request.ContentLength, ct);
        return Results.Ok(service.Preview(bytes));
    }

    private static async Task<IResult> Export(HttpRequest request, PresetTransferService service, CancellationToken ct)
    {
        var selection = await ReadControl<PresetExportRequest>(request, ct);
        var bytes = service.Export(selection);
        // Deliberately no save/title/path input in the download filename.
        return Results.File(bytes, "application/json", "Toolkit presets.toolkit-presets.json");
    }

    private static async Task<IResult> Resolve(HttpRequest request, PresetTransferService service, CancellationToken ct) =>
        Results.Ok(service.Resolve(await ReadControl<PresetImportResolution>(request, ct)));

    private static async Task<IResult> Commit(HttpRequest request, PresetTransferService service, CancellationToken ct) =>
        Results.Ok(service.Commit(await ReadControl<PresetImportCommit>(request, ct)));

    private static async Task<T> ReadControl<T>(HttpRequest request, CancellationToken ct)
    {
        LimitBody(request, ControlRequestLimit);
        // The same streaming guard also covers chunked requests before deserialization.
        var bytes = await PresetPackageCodec.ReadBoundedAsync(request.Body, request.ContentLength, ct);
        if (bytes.Length > ControlRequestLimit)
            throw new PresetTransferException("presetTransferRequestTooLarge", "Choose a smaller preset selection.", 413);
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(bytes, ControlJson)
                ?? throw new System.Text.Json.JsonException();
        }
        catch (System.Text.Json.JsonException)
        {
            throw new PresetTransferException("invalidPresetTransferRequest", "The preset selection or import decisions are malformed.", 400);
        }
    }

    private static void LimitBody(HttpRequest request, int limit)
    {
        if (request.ContentLength > limit)
            throw new PresetTransferException("presetTransferRequestTooLarge", $"The request exceeds the {limit / 1024} KiB limit.", 413);
        var feature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = limit;
    }

    private static readonly System.Text.Json.JsonSerializerOptions ControlJson = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };
}
