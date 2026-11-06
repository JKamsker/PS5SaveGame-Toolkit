using SaveToolkit.Api.Contracts;
using SaveToolkit.Core.Storage;
using SaveToolkit.LocalApi.Http;
using SaveToolkit.LocalApi.Protocol;
using SaveToolkit.LocalApi.Routing;
using SaveToolkit.Sdk.References;
using static SaveToolkit.LocalApi.Endpoints.EndpointFilters;

namespace SaveToolkit.LocalApi.Endpoints;

/// <summary>Reference packs and document lookups (ASP.NET: ReferenceEndpoints, ReferenceFieldEndpoints).</summary>
internal static class ReferenceRoutes
{
    private const string Packs = "/api/reference-packs";
    private const string Document = DocumentRoutes.Document;

    public static void Map(RouteTable routes)
    {
        routes.Map("GET", Packs, c => c.RunAsync([], null, () => Sync(() =>
            new ReferencePacksDto([.. c.Services.Packs.Packs.Select(p => p.Info)], c.Services.Packs.LoadErrors))));
        routes.Map("POST", Packs, c => c.RunAsync([], null, () => Sync(() => ImportPack(c))));
        routes.Map("DELETE", Packs + "/{packId}", c => c.RunAsync([], null, () => Sync(() =>
            IsCrossSite(c.Http) ? CrossSiteRejected()
            : c.Services.Packs.Remove(c.Route("packId")) ? new EmptyResult(204) : new EmptyResult(404))));

        routes.Map("GET", Document + "/reference-fields/candidates", c =>
        {
            var d = c.Document();
            var valueId = c.QueryString("valueId");
            var q = c.OptionalQueryString("q");
            var category = c.OptionalQueryString("category");
            var offset = c.OptionalQueryInt("offset");
            var limit = c.OptionalQueryInt("limit");
            var revision = c.OptionalQueryLong("revision");
            var actionId = c.OptionalQueryString("actionId");
            var sortBy = c.OptionalQueryEnum<ReferenceSortKey>("sortBy");
            var descending = c.OptionalQueryBool("descending");
            return c.RunAsync([], d, () => Sync(() =>
                ReferenceOperations.Candidates(d.Value, valueId, q, category, offset, limit, revision, actionId, sortBy, descending)));
        });
        routes.Map("GET", Document + "/reference-fields/record", c =>
        {
            var d = c.Document();
            var valueId = c.QueryString("valueId");
            var rawId = c.QueryString("rawId");
            var revision = c.OptionalQueryLong("revision");
            var actionId = c.OptionalQueryString("actionId");
            return c.RunAsync([], d, () => Sync(() => d.Value.FindReference(valueId, rawId, revision, actionId)));
        });
        routes.Map("POST", Document + "/reference-fields/records", c => DocumentRoutes.Json<ReferenceRecordsRequest>(
            c, "ReferenceRecordsRequest", (d, r) => ReferenceOperations.Records(d.Value, r)), json: true);
        routes.Map("GET", Document + "/sources", c => DocumentRoutes.Get(c, d => ReferenceOperations.Sources(d.Workspace, d.Value)));
        routes.Map("GET", Document + "/references/{catalogId}", c =>
        {
            var d = c.Document();
            var catalogId = c.Route("catalogId");
            var q = c.OptionalQueryString("q");
            var category = c.OptionalQueryString("category");
            var sortBy = c.OptionalQueryEnum<ReferenceSortKey>("sortBy");
            var descending = c.OptionalQueryBool("descending");
            var offset = c.OptionalQueryInt("offset");
            var limit = c.OptionalQueryInt("limit");
            return c.RunAsync([], d, () => Sync(() =>
                ReferenceOperations.Search(d.Workspace, d.Value, catalogId, q, category, sortBy, descending, offset, limit)));
        });
        routes.Map("GET", Document + "/references/{catalogId}/record", c =>
        {
            var d = c.Document();
            var catalogId = c.Route("catalogId");
            var key = c.QueryString("key");
            return c.RunAsync([], d, () => Sync(() => ReferenceOperations.Record(d.Workspace, d.Value, catalogId, key)));
        });
        routes.Map("GET", Document + "/reference-assets", c =>
        {
            var d = c.Document();
            var id = c.QueryString("id");
            return c.RunAsync([], d, () => Sync(() => Asset(c, d, id)));
        });
    }

    private static LocalResult Asset(EndpointContext c, DocumentRef d, string id)
    {
        var asset = d.Workspace.References(d.Value.Id).AssetForHost(id);
        if (asset is null || asset.Content.Length > c.Services.Packs.Options.Limits.MaxAssetBytes) return new EmptyResult(404);
        c.Http.SetResponseHeader("X-Content-Type-Options", "nosniff");
        c.Http.SetResponseHeader("Content-Security-Policy", "default-src 'none'");
        c.Http.SetResponseHeader("Cache-Control", "private, max-age=3600");
        return new BytesResult(200, asset.Content, asset.ContentType);
    }

    private static object ImportPack(EndpointContext c)
    {
        if (IsCrossSite(c.Http)) return CrossSiteRejected();
        if (MediaType.Parse(c.Http.ContentType) is not { IsForm: true } contentType)
            return new ApiOutcome(400, new ErrorDto("Expected multipart/form-data with one pack archive (.zip)."));

        var form = UploadReader.Read(c, contentType, ApiLimits.MaxReferencePackBody);
        if (form.Files is not [var file]) return new ApiOutcome(400, new ErrorDto("Upload exactly one pack archive."));
        var limits = c.Services.Packs.Options.Limits;
        if (file.Length > limits.MaxArchiveBytes)
            return ApiOutcome.Unprocessable(new ErrorDto(string.Create(System.Globalization.CultureInfo.CurrentCulture,
                $"The pack is larger than the limit of {limits.MaxArchiveBytes:N0} bytes.")));

        var pack = c.Services.Packs.Import(file.Content.ToArray(), Path.GetFileName(file.FileName));
        return ApiOutcome.Ok(pack.Info);
    }

    /// <summary>
    /// Packs change what every later document shows, so only the app's own pages may import or remove them
    /// (a cross-site form POST needs no CORS preflight).
    /// </summary>
    private static bool IsCrossSite(LocalHttpContext http)
    {
        var site = string.Join(",", http.HeaderValues("Sec-Fetch-Site"));
        if (site.Length > 0 && site is not ("same-origin" or "none")) return true;
        var origin = string.Join(",", http.HeaderValues("Origin"));
        var host = http.Header("Host") ?? string.Empty;
        return origin.Length > 0
            && (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !string.Equals(uri.Authority, host, StringComparison.OrdinalIgnoreCase));
    }

    private static ApiOutcome CrossSiteRejected() => new(403, new ErrorDto(ReferenceOperations.CrossSiteMessage));
}
