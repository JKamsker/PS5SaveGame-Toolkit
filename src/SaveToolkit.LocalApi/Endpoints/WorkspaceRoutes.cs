using SaveToolkit.Api.Contracts;
using SaveToolkit.Api.Endpoints;
using SaveToolkit.Core.Catalog;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Storage;
using SaveToolkit.LocalApi.Http;
using SaveToolkit.LocalApi.Protocol;
using SaveToolkit.LocalApi.Routing;
using static SaveToolkit.LocalApi.Endpoints.EndpointFilters;

namespace SaveToolkit.LocalApi.Endpoints;

/// <summary>Discovery and workspace routes (ASP.NET: WorkspaceEndpoints, DocumentGroupEndpoints, VersionEndpoints).</summary>
internal static class WorkspaceRoutes
{
    private const string Workspace = "/api/workspaces/{workspaceId}";

    public static void Map(RouteTable routes)
    {
        routes.Map("GET", "/api/version", c => c.RunAsync([], null, () => Sync(() => new VersionDto(c.Services.Version))));
        routes.Map("GET", "/api/plugins", c => c.RunAsync([], null, () => Sync(() => c.Services.Catalog.Plugins
            .OrderBy(p => p.Info.Name, StringComparer.OrdinalIgnoreCase).Select(p => p.ToDto()))));
        routes.Map("GET", "/api/titles", c => c.RunAsync([], null, () => Sync(() => TitleCatalog.Build(c.Services.Catalog))));
        routes.Map("GET", "/api/workspaces", c => c.RunAsync([], null,
            () => Sync(() => c.Services.Workspaces.All.Select(w => w.ToSummary()))));
        routes.Map("POST", "/api/workspaces", c => c.RunAsync([], null, () => Sync(() => CreateWorkspace(c))));

        routes.Map("GET", Workspace, c =>
        {
            var w = c.Workspace();
            return c.RunAsync([], null, () => Sync(() => w.Value.ToDto(c.Services.Catalog)));
        });
        routes.Map("DELETE", Workspace, c => c.RunAsync([], null, () => Sync(() =>
            c.Services.Workspaces.Remove(c.Route("workspaceId")) ? new EmptyResult(204) : new EmptyResult(404))));
        routes.Map("GET", Workspace + "/directories/{directoryId}/icon", c =>
        {
            var w = c.Workspace();
            var directoryId = c.Route("directoryId");
            return c.RunAsync([], null, async () =>
                w.Value.Set.FindDirectory(directoryId) is { } dir && DtoMapper.Icon(dir) is { } icon
                    ? new BytesResult(200, await ReadAllAsync(icon.OpenRead(), c.Aborted), "image/png")
                    : new EmptyResult(404));
        });
        routes.Map("POST", Workspace + "/files/{fileId}/open", c =>
        {
            var w = c.Workspace();
            var request = c.Body<OpenRequest>("OpenRequest", "request", optional: true, implicitBody: false);
            var fileId = c.Route("fileId");
            return c.RunAsync([request], null, () => Sync(() =>
                w.Value.Open(fileId, request?.PluginId, out var view, c.Aborted).ToDto(initialView: view)));
        }, json: true);
        routes.Map("GET", Workspace + "/files/{fileId}/group-proposals", c =>
        {
            var w = c.Workspace();
            var fileId = c.Route("fileId");
            var pluginId = c.QueryString("pluginId");
            return c.RunAsync([], null, () => Sync(() => w.Value.ProposeGroups(fileId, pluginId)));
        });
        routes.Map("POST", Workspace + "/document-groups", c =>
        {
            var w = c.Workspace();
            var request = c.Body<OpenGroupRequest>("OpenGroupRequest", "request")!;
            return c.RunAsync([request], null, () => Sync(() =>
                w.Value.OpenGroup(request.ProposalToken, out var view, request.ReleaseCleanDocumentId).Session.ToDto(w.Value, view)));
        }, json: true);
        routes.Map("POST", Workspace + "/export/plan", c =>
        {
            var w = c.Workspace();
            var request = c.Body<ExportRequest>("ExportRequest", "request")!;
            return c.RunAsync([request], null, () => Sync(() =>
            {
                var plan = WorkspaceExporter.Plan(w.Value, ExportNaming.ToOptions(request), c.Aborted);
                return new ExportPlanDto(plan.Files, plan.Overrides.Count);
            }));
        }, json: true);
        routes.Map("POST", Workspace + "/export", c =>
        {
            var w = c.Workspace();
            var request = c.Body<ExportRequest>("ExportRequest", "request")!;
            return c.RunAsync([request], null, () => Sync(() => Export(c, w, request)));
        }, json: true);
    }

    private static object Export(EndpointContext c, WorkspaceRef w, ExportRequest request)
    {
        var plan = WorkspaceExporter.Plan(w.Value, ExportNaming.ToOptions(request), c.Aborted);
        if (ExportNaming.NothingToExport(plan, request))
            return ApiOutcome.Unprocessable(new ErrorDto(ExportNaming.NothingChangedMessage));

        c.Aborted.ThrowIfCancellationRequested();
        using var zip = new MemoryStream();
        WorkspaceExporter.WriteZip(plan, zip, c.Aborted);
        c.Aborted.ThrowIfCancellationRequested();
        return new BytesResult(200, zip.ToArray(), "application/zip", ExportNaming.DownloadName(w.Value.Name));
    }

    private static ApiOutcome CreateWorkspace(EndpointContext c)
    {
        if (MediaType.Parse(c.Http.ContentType) is not { IsForm: true } contentType)
            return new ApiOutcome(400, new ErrorDto("Expected multipart/form-data with one or more files."));

        var form = UploadReader.Read(c, contentType, ApiLimits.MaxUploadBytes);
        if (form.Files.Count == 0)
            return new ApiOutcome(400, new ErrorDto("No files uploaded."));

        var name = form.First("name") is { Length: > 0 } n ? n : Path.GetFileNameWithoutExtension(form.Files[0].FileName);
        var sources = form.Files.Select(f => new ImportSource(f.FileName, () => UploadReader.OpenRead(f.Content))).ToList();
        var workspace = c.Services.Workspaces.Create(name, sources, c.Aborted);
        return ApiOutcome.Ok(c.Retain(workspace.Id).ToDto(c.Services.Catalog));
    }

    internal static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken cancellationToken)
    {
        await using (stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
    }
}

/// <summary>The <c>/api/version</c> body (<c>{version}</c>).</summary>
internal sealed record VersionDto(string Version);
