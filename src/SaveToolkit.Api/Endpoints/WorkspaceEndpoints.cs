using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Core;
using SaveToolkit.Core.Catalog;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Collections;

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

        ws.MapDocumentGroups();

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
        doc.MapActionReview();
        doc.MapGet("", (DocumentRef d) => d.Value.ToDto(d.Workspace));
        doc.MapDelete("", (DocumentRef d) => d.Workspace.Close(d.Value.Id) ? Results.NoContent() : Results.NotFound());
        doc.MapGet("/nodes", (DocumentRef d, [FromQuery] string id) => d.Value.Expand(id));
        // Opaque IDs may contain slashes, which Kestrel preserves as %2F in route parameters.
        doc.MapGet("/collections", (HttpRequest request, DocumentRef document, [FromQuery] string collectionId) =>
            QueryCollection(request, document, collectionId));
        doc.MapGet("/collections/{collectionId}", QueryCollection);
        doc.MapPost("/edits", (DocumentRef d, EditRequest request) =>
            ToResult(d.Value.ApplyEdits([.. request.Edits.Select(e => new IncomingEdit(e.ValueId, e.Value))], request.Revision)));
        doc.MapPost("/actions", (DocumentRef d, ActionRequest request) =>
            ToResult(d.Value.InvokeAction(request.ActionId, request.Parameters ?? [], request.Revision)));
        doc.MapPost("/undo", (DocumentRef d) => ToResult(d.Value.Undo()));
        doc.MapPost("/redo", (DocumentRef d) => ToResult(d.Value.Redo()));
        doc.MapGet("/assets", (DocumentRef d, [FromQuery] string id) =>
            d.Value.GetAsset(id) is { } asset ? Results.Bytes(asset.Content.ToArray(), asset.ContentType) : Results.NotFound());
        doc.MapGet("/blobs", (DocumentRef d, [FromQuery] string id, [FromQuery] long offset, [FromQuery] int count) =>
            offset < 0
                ? (IResult)Results.BadRequest(new ErrorDto("Offset cannot be negative."))
                : Results.Bytes(d.Value.ReadBlob(id, offset, Math.Clamp(count, 0, MaxBlobPage)).ToArray(), "application/octet-stream"));
        doc.MapGet("/download", (DocumentRef d) =>
        {
            var session = d.Value;
            if (d.Workspace.DocumentGroups.Any(g => g.Session.Id == session.Id))
                return Results.Conflict(new ErrorDto("Use workspace export to download all group members atomically."));
            var own = session.Serialize().FirstOrDefault(o => o.RelativePath == session.File.RelativePath);
            return own is null
                ? Results.Stream(session.File.OpenRead(), "application/octet-stream", session.File.FileName)
                : Results.File(own.Content.ToArray(), "application/octet-stream", session.File.FileName);
        });
    }

    private static IResult QueryCollection(HttpRequest request, DocumentRef document, string collectionId)
    {
        if (!TryInt(request, "offset", 0, out var offset) || !TryInt(request, "limit", QueryableCollectionLimits.DefaultPageSize, out var limit))
        {
            return Results.BadRequest(new ErrorDto("Offset and limit must be integers."));
        }

        if (limit is < 1 or > QueryableCollectionLimits.MaxPageSize)
        {
            return Results.BadRequest(new ErrorDto($"Page size must be between 1 and {QueryableCollectionLimits.MaxPageSize}."));
        }

        if (offset < 0)
        {
            return Results.BadRequest(new ErrorDto("Offset cannot be negative."));
        }

        if (!TryBool(request, "descending", false, out var descending) || !TryLong(request, "revision", out var revision))
        {
            return Results.BadRequest(new ErrorDto("Descending and revision must be valid values."));
        }

        var filters = ParseFilters(request);
        if (filters is null)
        {
            return Results.BadRequest(new ErrorDto("Filters must be a JSON object of string values."));
        }

        var query = new QueryableCollectionQuery
        {
            Text = request.Query["q"].FirstOrDefault() ?? request.Query["text"].FirstOrDefault(),
            Filters = filters,
            SortColumn = request.Query["sort"].FirstOrDefault(),
            Descending = descending,
            Offset = offset,
            Limit = limit,
            Cursor = request.Query["cursor"].FirstOrDefault(),
        };
        if (query.Text is { Length: > QueryableCollectionLimits.MaxQueryLength }
            || query.Cursor is { Length: 0 or > QueryableCollectionLimits.MaxCursorLength }
            || query.Cursor is not null && offset != 0
            || query.Filters.Count > QueryableCollectionLimits.MaxFilterCount
            || query.Filters.Any(f => f.Key.Length == 0 || f.Key.Length > 128 || f.Value.Length > QueryableCollectionLimits.MaxFilterLength))
        {
            return Results.BadRequest(new ErrorDto("Collection query exceeds the protocol limits."));
        }

        QueryableCollectionPage page;
        try
        {
            page = document.Value.QueryCollection(collectionId, query, revision);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ErrorDto(ex.Message));
        }

        var dto = QueryablePageDto.From(page);
        var body = JsonSerializer.SerializeToUtf8Bytes(dto, ToolkitJson.Options);
        const int maxResponseBytes = 2 * 1024 * 1024;
        return body.Length > maxResponseBytes
            ? Results.Json(new ErrorDto("The collection page exceeds the response size limit."), statusCode: StatusCodes.Status413PayloadTooLarge, options: ToolkitJson.Options)
            : Results.Bytes(body, "application/json");
    }

    private static Dictionary<string, string>? ParseFilters(HttpRequest request)
    {
        var filters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.Query["filters"].FirstOrDefault() is { Length: > 0 } json)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json, ToolkitJson.Options);
                if (parsed is null) return null;
                foreach (var (key, value) in parsed) filters[key] = value;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        foreach (var item in request.Query.Where(p => p.Key.StartsWith("filter.", StringComparison.OrdinalIgnoreCase)))
        {
            var key = item.Key["filter.".Length..];
            if (key.Length == 0 || item.Value.Count == 0) return null;
            filters[key] = item.Value[^1]!;
        }

        return filters;
    }

    private static bool TryInt(HttpRequest request, string name, int fallback, out int value)
    {
        if (!request.Query.TryGetValue(name, out var raw))
        {
            value = fallback;
            return true;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryLong(HttpRequest request, string name, out long? value)
    {
        value = null;
        if (!request.Query.TryGetValue(name, out var raw)) return true;
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return false;
        value = parsed;
        return true;
    }

    private static bool TryBool(HttpRequest request, string name, bool fallback, out bool value)
    {
        if (!request.Query.TryGetValue(name, out var raw))
        {
            value = fallback;
            return true;
        }

        return bool.TryParse(raw, out value);
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
