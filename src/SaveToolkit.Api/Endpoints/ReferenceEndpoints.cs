using Microsoft.AspNetCore.Mvc;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Api.Endpoints;

/// <summary>Reference packs (import/list/remove) and per-document sources, lookups and assets.</summary>
internal static class ReferenceEndpoints
{
    public static void MapReferenceApi(this IEndpointRouteBuilder app)
    {
        var packs = app.MapGroup("/api/reference-packs");
        packs.MapGet("", (ReferencePackStore store) => new ReferencePacksDto([.. store.Packs.Select(p => p.Info)], store.LoadErrors));
        packs.MapPost("", ImportPack).DisableAntiforgery();
        packs.MapDelete("/{packId}", (string packId, ReferencePackStore store) => store.Remove(packId) ? Results.NoContent() : Results.NotFound());

        var doc = app.MapGroup("/api/workspaces/{workspaceId}/documents/{documentId}");
        doc.MapGet("/sources", (DocumentRef d) =>
            new DocumentSourcesDto([.. d.Workspace.References(d.Value.Id).Datasets().Select(s => s.ToDto())]));
        doc.MapGet("/references/{catalogId}", Search);
        doc.MapGet("/references/{catalogId}/record", (DocumentRef d, string catalogId, [FromQuery] string key) =>
        {
            var catalog = Catalog(d, catalogId);
            var record = catalog.Find(key) ?? throw new KeyNotFoundException($"No record '{key}' in catalog '{catalogId}'.");
            return new ReferenceRecordDetailDto(record.ToDto(), catalog.Pack.Title, catalog.Pack.Version, record.Dataset);
        });
        doc.MapGet("/reference-assets", (DocumentRef d, [FromQuery] string id, ReferencePackStore store, HttpContext http) =>
        {
            var asset = d.Workspace.References(d.Value.Id).AssetForHost(id);
            if (asset is null || asset.Content.Length > store.Options.Limits.MaxAssetBytes)
            {
                return Results.NotFound();
            }

            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.ContentSecurityPolicy = "default-src 'none'";
            http.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.Bytes(asset.Content.ToArray(), asset.ContentType);
        });
    }

    private static ReferencePageDto Search(
        DocumentRef d,
        string catalogId,
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] int? offset,
        [FromQuery] int? limit)
    {
        var catalog = Catalog(d, catalogId);
        var query = new ReferenceQuery
        {
            Text = q,
            Category = string.IsNullOrEmpty(category) ? null : category,
            Offset = Math.Max(0, offset ?? 0),
            Limit = Math.Clamp(limit ?? ReferenceQuery.DefaultLimit, 1, ReferenceQuery.MaxLimit),
        };
        var page = catalog.Search(query);
        return new ReferencePageDto(catalogId, catalog.Pack.Id, [.. page.Items.Select(r => r.ToDto())], page.Total, page.Offset, query.Limit, catalog.Categories);
    }

    private static IReferenceCatalog Catalog(DocumentRef d, string catalogId) =>
        d.Workspace.References(d.Value.Id).CatalogForHost(catalogId)
        ?? throw new KeyNotFoundException($"No reference pack for this document provides catalog '{catalogId}'.");

    private static async Task<IResult> ImportPack(HttpRequest request, ReferencePackStore store, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new ErrorDto("Expected multipart/form-data with one pack archive (.zip)."));
        }

        var form = await request.ReadFormAsync(ct);
        if (form.Files is not [var file])
        {
            return Results.BadRequest(new ErrorDto("Upload exactly one pack archive."));
        }

        if (file.Length > store.Options.Limits.MaxArchiveBytes)
        {
            return Results.UnprocessableEntity(new ErrorDto($"The pack is larger than the limit of {store.Options.Limits.MaxArchiveBytes:N0} bytes."));
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var pack = await Task.Run(() => store.Import(buffer.ToArray(), Path.GetFileName(file.FileName)), ct);
        return Results.Ok(pack.Info);
    }
}
