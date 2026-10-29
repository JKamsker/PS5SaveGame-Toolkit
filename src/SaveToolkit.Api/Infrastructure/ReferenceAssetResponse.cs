using SaveToolkit.Sdk;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>HTTP response for immutable assets owned by a loaded reference pack.</summary>
internal static class ReferenceAssetResponse
{
    internal static IResult Create(AssetContent asset, HttpResponse response)
    {
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.ContentSecurityPolicy = "default-src 'none'";
        response.Headers.CacheControl = "private, max-age=3600";
        return Results.Bytes(asset.Content, asset.ContentType);
    }
}
