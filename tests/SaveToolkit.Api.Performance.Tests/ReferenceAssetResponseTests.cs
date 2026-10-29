using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Sdk;

namespace SaveToolkit.Api.Performance.Tests;

public sealed class ReferenceAssetResponseTests
{
    [Fact]
    public void ImmutablePackAssetIsServedWithoutCopyingItsMemorySlice()
    {
        var bytes = new byte[1024 * 1024 + 2];
        bytes[0] = 17;
        bytes[1] = 42;
        bytes[^1] = 99;
        var content = bytes.AsMemory(1, bytes.Length - 2);
        var context = new DefaultHttpContext();

        var result = Assert.IsType<FileContentHttpResult>(ReferenceAssetResponse.Create(new AssetContent("image/png", content), context.Response));

        Assert.True(result.FileContents.Equals(content), "The asset response must retain the existing immutable memory slice.");
        Assert.Equal(content.Length, result.FileContents.Length);
        Assert.Equal(42, result.FileContents.Span[0]);
        Assert.Equal(0, result.FileContents.Span[^1]);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions);
        Assert.Equal("default-src 'none'", context.Response.Headers.ContentSecurityPolicy);
        Assert.Equal("private, max-age=3600", context.Response.Headers.CacheControl);
    }
}
