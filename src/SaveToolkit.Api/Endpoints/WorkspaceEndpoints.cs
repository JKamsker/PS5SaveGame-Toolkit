using Microsoft.AspNetCore.Mvc;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Core.Catalog;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Api.Endpoints;

internal static class WorkspaceEndpoints
{
    private const int MaxBlobPage = 64 * 1024;

    public static void MapToolkitApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/plugins", (WorkspaceStore store) =>
            store.Catalog.Plugins.OrderBy(p => p.Info.Name, StringComparer.OrdinalIgnoreCase).Select(p => p.ToDto()));

        api.MapGet("/titles", (WorkspaceStore store) => TitleCatalog.Build(store.Catalog));

        api.MapGet("/workspaces", (WorkspaceStore store) => store.All.Select(w => w.ToSummary()));

        api.MapPost("/workspaces", CreateWorkspace).DisableAntiforgery();

        var ws = api.MapGroup("/workspaces/{workspaceId}");
        ws.MapGet("", (WorkspaceRef w, WorkspaceStore store) => w.Value.ToDto(store.Catalog));
        ws.MapDelete("", (string workspaceId, WorkspaceStore store) => store.Remove(workspaceId) ? Results.NoContent() : Results.NotFound());

        ws.MapGet("/directories/{directoryId}/icon", (WorkspaceRef w, string directoryId) =>
            w.Value.Set.FindDirectory(directoryId) is { } dir && DtoMapper.Icon(dir) is { } icon
                ? Results.Stream(icon.OpenRead(), "image/png")
                : Results.NotFound());

        ws.MapPost("/files/{fileId}/open", (WorkspaceRef w, string fileId, [FromBody] OpenRequest? request) =>
            w.Value.Open(fileId, request?.PluginId).ToDto());

        MapDocumentRoutes(ws.MapGroup("/documents/{documentId}"));

        ws.MapPost("/export/plan", (WorkspaceRef w, ExportRequest request) =>
        {
            var plan = WorkspaceExporter.Plan(w.Value, ToOptions(request));
            return new ExportPlanDto(plan.Files, plan.Overrides.Count);
        });
        ws.MapPost("/export", (WorkspaceRef w, ExportRequest request) =>
        {
            var plan = WorkspaceExporter.Plan(w.Value, ToOptions(request));
            if (plan.Overrides.Count == 0 && request.Scope != ExportScope.Everything)
            {
                return Results.UnprocessableEntity(new ErrorDto("Nothing has been changed yet."));
            }

            var buffer = new MemoryStream();
            WorkspaceExporter.WriteZip(plan, buffer);
            buffer.Position = 0;
            return Results.File(buffer, "application/zip", $"{SafeName(w.Value.Name)}-edited.zip");
        });
    }

    private static void MapDocumentRoutes(RouteGroupBuilder doc)
    {
        doc.MapGet("", (DocumentRef d) => d.Value.ToDto());
        doc.MapDelete("", (DocumentRef d) => d.Workspace.Close(d.Value.Id) ? Results.NoContent() : Results.NotFound());
        doc.MapGet("/nodes", (DocumentRef d, [FromQuery] string id) => d.Value.Expand(id));
        doc.MapPost("/edits", (DocumentRef d, EditRequest request) =>
            ToResult(d.Value.ApplyEdits([.. request.Edits.Select(e => new IncomingEdit(e.ValueId, e.Value))], request.Revision)));
        doc.MapPost("/actions", (DocumentRef d, ActionRequest request) =>
            ToResult(d.Value.InvokeAction(request.ActionId, request.Parameters ?? [], request.Revision)));
        doc.MapPost("/undo", (DocumentRef d) => ToResult(d.Value.Undo()));
        doc.MapPost("/redo", (DocumentRef d) => ToResult(d.Value.Redo()));
        doc.MapGet("/assets", (DocumentRef d, [FromQuery] string id) =>
            d.Value.GetAsset(id) is { } asset ? Results.Bytes(asset.Content.ToArray(), asset.ContentType) : Results.NotFound());
        doc.MapGet("/blobs", (DocumentRef d, [FromQuery] string id, [FromQuery] long offset, [FromQuery] int count) =>
            Results.Bytes(d.Value.ReadBlob(id, offset, Math.Clamp(count, 0, MaxBlobPage)).ToArray(), "application/octet-stream"));
        doc.MapGet("/download", (DocumentRef d) =>
        {
            var session = d.Value;
            var own = session.Serialize().FirstOrDefault(o => o.RelativePath == session.File.RelativePath);
            return own is null
                ? Results.Stream(session.File.OpenRead(), "application/octet-stream", session.File.FileName)
                : Results.File(own.Content.ToArray(), "application/octet-stream", session.File.FileName);
        });
    }

    private static async Task<IResult> CreateWorkspace(HttpRequest request, WorkspaceStore store, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new ErrorDto("Expected multipart/form-data with one or more files."));
        }

        var form = await request.ReadFormAsync(ct);
        if (form.Files.Count == 0)
        {
            return Results.BadRequest(new ErrorDto("No files uploaded."));
        }

        var name = form["name"].FirstOrDefault() is { Length: > 0 } n ? n : Path.GetFileNameWithoutExtension(form.Files[0].FileName);
        var sources = form.Files.Select(f => new ImportSource(f.FileName, f.OpenReadStream)).ToList();
        var workspace = await Task.Run(() => store.Create(name, sources), ct);
        return Results.Ok(workspace.ToDto(store.Catalog));
    }

    private static IResult ToResult(SessionEditResult result) =>
        result.Ok ? Results.Ok(result)
        : result.Stale ? Results.Conflict(result)
        : Results.UnprocessableEntity(result);

    private static ExportOptions ToOptions(ExportRequest r) => new() { Scope = r.Scope, MirrorBackups = r.MirrorBackups };

    private static string SafeName(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
}
