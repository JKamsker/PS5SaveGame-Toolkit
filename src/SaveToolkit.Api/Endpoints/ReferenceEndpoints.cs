using Microsoft.AspNetCore.Mvc;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Api.Endpoints;

/// <summary>Reference packs (import/list/remove) and per-document sources, lookups and assets.</summary>
internal static class ReferenceEndpoints
{
    /// <summary>Default archive limit plus room for the multipart envelope; checked again against the store's limits.</summary>
    private static readonly long MaxImportBody = new ReferencePackLimits().MaxArchiveBytes + (1024 * 1024);

    public static void MapReferenceApi(this IEndpointRouteBuilder app)
    {
        var packs = app.MapGroup("/api/reference-packs");
        packs.MapGet("", (ReferencePackStore store) => new ReferencePacksDto([.. store.Packs.Select(p => p.Info)], store.LoadErrors));
        packs.MapPost("", ImportPack)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxImportBody), new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxImportBody });
        packs.MapDelete("/{packId}", (string packId, HttpRequest request, ReferencePackStore store) =>
            IsCrossSite(request) ? CrossSiteRejected() : store.Remove(packId) ? Results.NoContent() : Results.NotFound());

        var doc = app.MapGroup("/api/workspaces/{workspaceId}/documents/{documentId}");
        doc.MapReferenceFieldApi();
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
        [FromQuery] ReferenceSortKey? sortBy,
        [FromQuery] bool? descending,
        [FromQuery] int? offset,
        [FromQuery] int? limit)
    {
        var catalog = Catalog(d, catalogId);
        ReferenceSortBinding.Validate(sortBy);
        var query = new ReferenceQuery
        {
            Text = q,
            Category = string.IsNullOrEmpty(category) ? null : category,
            SortBy = sortBy,
            Descending = descending ?? false,
            Offset = Math.Max(0, offset ?? 0),
            Limit = Math.Clamp(limit ?? ReferenceQuery.DefaultLimit, 1, ReferenceQuery.MaxLimit),
        };
        var page = catalog.Search(query);
        return new ReferencePageDto(catalogId, catalog.Pack.Id, [.. page.Items.Select(r => r.ToDto())], page.Total, page.Offset, query.Limit, catalog.Categories)
        {
            SortKeys = catalog.SortKeys,
        };
    }

    private static IReferenceCatalog Catalog(DocumentRef d, string catalogId) =>
        d.Workspace.References(d.Value.Id).CatalogForHost(catalogId)
        ?? throw new KeyNotFoundException($"No reference pack for this document provides catalog '{catalogId}'.");

    private static async Task<IResult> ImportPack(HttpRequest request, ReferencePackStore store, CancellationToken ct)
    {
        if (IsCrossSite(request))
        {
            return CrossSiteRejected();
        }

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

    /// <summary>
    /// Packs change what every later document shows, so only the app's own pages may import or remove them
    /// (a cross-site form POST needs no CORS preflight).
    /// </summary>
    private static bool IsCrossSite(HttpRequest request)
    {
        var site = request.Headers["Sec-Fetch-Site"].ToString();
        if (site.Length > 0 && site is not ("same-origin" or "none"))
        {
            return true;
        }

        var origin = request.Headers.Origin.ToString();
        return origin.Length > 0
            && (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static IResult CrossSiteRejected() =>
        Results.Json(new ErrorDto("Reference packs can only be changed from the toolkit itself."), statusCode: StatusCodes.Status403Forbidden);
}
