using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class ApiExceptionContractTests
{
    [Theory]
    [InlineData("missing", 404)]
    [InlineData("save", 422)]
    [InlineData("import", 422)]
    [InlineData("export", 422)]
    [InlineData("pack", 422)]
    [InlineData("data", 422)]
    [InlineData("conflict", 409)]
    [InlineData("stale", 409)]
    [InlineData("request", 400)]
    [InlineData("size", 413)]
    [InlineData("projection-size", 413)]
    [InlineData("unexpected", 500)]
    public async Task Every_exception_mapping_retains_consistent_problem_fields(string failure, int status)
    {
        const string diagnostic = "Occurrence diagnostic";
        Exception exception = failure switch
        {
            "missing" => new KeyNotFoundException(diagnostic),
            "save" => new SaveFormatException(diagnostic),
            "import" => new ImportException(diagnostic),
            "export" => new ExportException(diagnostic),
            "pack" => new ReferencePackException(diagnostic),
            "data" => new InvalidDataException(diagnostic),
            "conflict" => new InvalidOperationException(diagnostic),
            "stale" => new StaleViewException(),
            "request" => new BadHttpRequestException(diagnostic),
            "size" => new BadHttpRequestException(diagnostic, StatusCodes.Status413PayloadTooLarge),
            "projection-size" => new FieldReferenceResponseLimitException(diagnostic),
            _ => new NotSupportedException(diagnostic),
        };
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        using var stream = new MemoryStream();
        context.Response.Body = stream;
        var handler = new ToolkitExceptionHandler(NullLogger<ToolkitExceptionHandler>.Instance);

        Assert.True(await handler.TryHandleAsync(context, exception, ApiContractFixture.Ct));
        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(stream.ToArray());
        var body = json.RootElement;
        Assert.Equal("about:blank", body.GetProperty("type").GetString());
        Assert.Equal(status, body.GetProperty("status").GetInt32());
        Assert.Equal(Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status), body.GetProperty("title").GetString());
        Assert.Equal(body.GetProperty("error").GetString(), body.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
        if (failure == "stale") Assert.Equal("stale", body.GetProperty("code").GetString());
        else if (status == 500) Assert.DoesNotContain(diagnostic, body.GetRawText(), StringComparison.Ordinal);
        else Assert.Contains(diagnostic, body.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }
}
