using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SaveToolkit.Api.Infrastructure;

namespace SaveToolkit.Api.Performance.Tests;

public sealed class ExceptionDiagnosticsTests
{
    [Theory]
    [InlineData("Could not open SQLite database image: database disk image is malformed.")]
    [InlineData("Kraken stream references data outside its bounded buffers.")]
    public async Task Invalid_save_data_is_not_misreported_as_an_archive_error(string message)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        using var body = new MemoryStream();
        context.Response.Body = body;
        var handler = new ToolkitExceptionHandler(NullLogger<ToolkitExceptionHandler>.Instance);

        Assert.True(await handler.TryHandleAsync(context, new InvalidDataException(message),
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal($"Invalid data: {message}", document.RootElement.GetProperty("error").GetString());
    }
}
