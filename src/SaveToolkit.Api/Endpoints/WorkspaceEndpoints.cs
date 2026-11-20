using Microsoft.AspNetCore.Mvc;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.Catalog;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.LocalApi.Protocol;
using SaveToolkit.Sdk.Collections;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace SaveToolkit.Api.Endpoints;

internal static class WorkspaceEndpoints
{

    public static void MapToolkitApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter<ApiContractFilter>()
            .WithApiErrors(Status499ClientClosedRequest, Status500InternalServerError);

        api.MapGet("/plugins", (WorkspaceStore store) =>
            store.Catalog.Plugins.OrderBy(p => p.Info.Name, StringComparer.OrdinalIgnoreCase).Select(p => p.ToDto()));

        api.MapGet("/titles", (WorkspaceStore store) => TitleCatalog.Build(store.Catalog));

        api.MapGet("/workspaces", (WorkspaceStore store) => store.All.Select(w => w.ToSummary()));

        api.MapPost("/workspaces", CreateWorkspace).DisableAntiforgery()
            .Produces<WorkspaceDto>().WithApiErrors(Status400BadRequest, Status413PayloadTooLarge, Status422UnprocessableEntity);

        var ws = api.MapGroup("/workspaces/{workspaceId}").WithApiErrors(Status404NotFound);
        ws.MapGet("", (WorkspaceRef w, WorkspaceStore store) => w.Value.ToDto(store.Catalog));
        ws.MapDelete("", (string workspaceId, WorkspaceStore store) => store.Remove(workspaceId) ? Results.NoContent() : Results.NotFound())
            .Produces(StatusCodes.Status204NoContent);

        ws.MapGet("/directories/{directoryId}/icon", (WorkspaceRef w, string directoryId) =>
            w.Value.Set.FindDirectory(directoryId) is { } dir && DtoMapper.Icon(dir) is { } icon
                ? Results.Stream(icon.OpenRead(), "image/png")
                : Results.NotFound()).Produces<byte[]>(contentType: "image/png");

        ws.MapPost("/files/{fileId}/open", (WorkspaceRef w, string fileId, [FromBody] OpenRequest? request, CancellationToken ct) =>
            w.Value.Open(fileId, request?.PluginId, out var view, ct).ToDto(initialView: view))
            .WithApiErrors(Status400BadRequest, Status409Conflict, Status413PayloadTooLarge, Status415UnsupportedMediaType, Status422UnprocessableEntity);

        ws.MapDocumentGroups();

        MapDocumentRoutes(ws.MapGroup("/documents/{documentId}"));

        ws.MapPost("/export/plan", (WorkspaceRef w, ExportRequest request, CancellationToken ct) =>
        {
            var plan = WorkspaceExporter.Plan(w.Value, ExportNaming.ToOptions(request), ct);
            return new ExportPlanDto(plan.Files, plan.Overrides.Count);
        }).WithApiErrors(Status400BadRequest, Status409Conflict, Status415UnsupportedMediaType, Status422UnprocessableEntity);
        ws.MapPost("/export", (WorkspaceRef w, ExportRequest request, CancellationToken ct) =>
        {
            var plan = WorkspaceExporter.Plan(w.Value, ExportNaming.ToOptions(request), ct);
            if (ExportNaming.NothingToExport(plan, request))
            {
                return Results.UnprocessableEntity(new ErrorDto(ExportNaming.NothingChangedMessage));
            }

            return WorkspaceExportResponse.Create(plan, ExportNaming.DownloadName(w.Value.Name), ct);
        }).Produces<byte[]>(contentType: "application/zip")
            .WithApiErrors(Status400BadRequest, Status409Conflict, Status415UnsupportedMediaType, Status422UnprocessableEntity);
    }

    private static void MapDocumentRoutes(RouteGroupBuilder doc)
    {
        doc.AddEndpointFilter<DocumentRevisionFilter>().WithApiErrors(Status400BadRequest, Status404NotFound, Status409Conflict,
            Status412PreconditionFailed, Status422UnprocessableEntity);
        doc.MapActionReview();
        doc.MapGet("", (DocumentRef d) => d.Value.ToDto(d.Workspace)).WithApiErrors(Status413PayloadTooLarge);
        doc.MapDelete("", (DocumentRef d) => d.Workspace.CloseClean(d.Value.Id) ? Results.NoContent() : Results.NotFound())
            .Produces(StatusCodes.Status204NoContent);
        doc.MapGet("/nodes", (DocumentRef d, [FromQuery] string id) => d.Value.Expand(id)).WithApiErrors(Status413PayloadTooLarge);
        // Opaque IDs may contain slashes, which Kestrel preserves as %2F in route parameters.
        doc.MapGet("/collections", (HttpRequest request, DocumentRef document, [FromQuery] string collectionId) =>
            QueryCollection(request, document, collectionId)).Produces<QueryablePageDto>().WithApiErrors(Status413PayloadTooLarge);
        doc.MapGet("/collections/{collectionId}", QueryCollection).Produces<QueryablePageDto>().WithApiErrors(Status413PayloadTooLarge);
        doc.MapPost("/edits", (DocumentRef d, EditRequest request, CancellationToken ct) =>
            SessionEditResponse.Create(d.Value.ApplyEdits([.. request.Edits.Select(e => new IncomingEdit(e.ValueId, e.Value))], request.Revision, ct)))
            .Produces<SessionEditResult>().WithApiErrors(Status415UnsupportedMediaType);
        doc.MapPost("/actions", (DocumentRef d, ActionRequest request, CancellationToken ct) =>
            SessionEditResponse.Create(d.Value.InvokeAction(request.ActionId, request.Parameters ?? [], request.Revision, ct)))
            .Produces<SessionEditResult>().WithApiErrors(Status415UnsupportedMediaType);
        doc.MapPost("/undo", (DocumentRef d, HistoryRequest? request) => SessionEditResponse.Create(d.Value.Undo()))
            .Produces<SessionEditResult>().WithApiErrors(Status415UnsupportedMediaType);
        doc.MapPost("/redo", (DocumentRef d, HistoryRequest? request) => SessionEditResponse.Create(d.Value.Redo()))
            .Produces<SessionEditResult>().WithApiErrors(Status415UnsupportedMediaType);
        doc.MapGet("/assets", (DocumentRef d, [FromQuery] string id) =>
            d.Value.GetAsset(id) is { } asset ? Results.Bytes(asset.Content, asset.ContentType) : Results.NotFound())
            .Produces<byte[]>(contentType: "application/octet-stream");
        doc.MapGet("/blobs", (DocumentRef d, [FromQuery] string id, [FromQuery] long offset, [FromQuery] int count) =>
            offset < 0
                ? (IResult)Results.BadRequest(new ErrorDto("Offset cannot be negative."))
                : Results.Bytes(d.Value.ReadBlob(id, offset, Math.Clamp(count, 0, ApiLimits.MaxBlobPage)), "application/octet-stream"))
            .Produces<byte[]>(contentType: "application/octet-stream");
        doc.MapGet("/download", (DocumentRef d, CancellationToken ct) =>
        {
            var session = d.Value;
            var own = DocumentDownloader.Capture(d.Workspace, session, ct);
            return own is null
                ? Results.Stream(session.File.OpenRead(), "application/octet-stream", session.File.FileName)
                : Results.Bytes(own.Content, "application/octet-stream", session.File.FileName);
        }).Produces<byte[]>(contentType: "application/octet-stream");
    }

    private static IResult QueryCollection(HttpRequest request, DocumentRef document, string collectionId)
    {
        var (query, revision) = BindCollectionQuery(request);
        IResult response;
        try
        {
            response = document.Value.QueryCollection(collectionId, query, revision,
                page => CollectionPageResponse.Create(QueryablePageDto.From(page), throwOnLimit: true), request.HttpContext.RequestAborted);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ErrorDto(ex.Message));
        }

        return response;
    }

    private static (QueryableCollectionQuery Query, long? Revision) BindCollectionQuery(HttpRequest request) =>
        CollectionQueryBinder.Bind(new QueryCollectionValues(request.Query));

    private static async Task<IResult> CreateWorkspace(HttpRequest request, WorkspaceStore store, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new ErrorDto("Expected multipart/form-data with one or more files."));
        }

        var form = await MultipartUpload.ReadAsync(request, ct);
        if (form.Files.Count == 0)
        {
            return Results.BadRequest(new ErrorDto("No files uploaded."));
        }

        var name = form["name"].FirstOrDefault() is { Length: > 0 } n ? n : Path.GetFileNameWithoutExtension(form.Files[0].FileName);
        var sources = form.Files.Select(f => new ImportSource(f.FileName, f.OpenReadStream)).ToList();
        var workspace = await Task.Run(() => store.Create(name, sources, ct), ct);
        return Results.Ok(WorkspaceRef.Retain(request.HttpContext, store, workspace.Id).ToDto(store.Catalog));
    }
}
