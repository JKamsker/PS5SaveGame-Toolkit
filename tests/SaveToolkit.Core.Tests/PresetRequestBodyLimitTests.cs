using Microsoft.AspNetCore.Http;
using SaveToolkit.Api.Infrastructure;

namespace SaveToolkit.Core.Tests;

public sealed class PresetRequestBodyLimitTests
{
    [Fact]
    public async Task LengthlessBodyIsRejectedBeforeTheNextComponent()
    {
        var called = false;
        var context = new DefaultHttpContext
        {
            Response = { Body = new MemoryStream() },
        };
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new PresetRequestBodyLimitMetadata(8)),
            "preset"));
        context.Request.Body = new MemoryStream(new byte[9]);
        context.Request.ContentLength = null;

        await new PresetRequestBodyLimitMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }).InvokeAsync(context);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.False(called);
    }
}
