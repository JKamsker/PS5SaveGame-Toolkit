using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.Presets;

namespace SaveToolkit.Core.Tests;

/// <summary>Exercises the composed routing/middleware/handler pipeline with observable request streams.</summary>
public sealed class PresetTransferRequestLimitTests(IsolatedApiFactory factory) : IClassFixture<IsolatedApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public async Task TransferKeepsItsOwnExactCapsAndReadsOnlyCapPlusOne(bool declaredLength, bool package, bool chunked)
    {
        var root = TestZips.TempDir();
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("Toolkit:PresetRoot", root));
        using var client = host.CreateClient();
        var store = host.Services.GetRequiredService<PresetStore>();
        var source = PresetTransferFixture.Preset();
        var saved = store.Create(source.Name, source.Compatibility, source.Actions);
        var before = File.ReadAllBytes(Path.Combine(root, "presets.json"));
        var limit = package ? PresetPackageCodec.MaxBytes : PresetTransferLimits.MaxControlBytes;
        var path = "/api/presets/transfer/" + (package ? "preview" : "export");
        var valid = package ? PresetPackageCodec.Encode([source])
            : JsonSerializer.SerializeToUtf8Bytes(new PresetExportRequest([saved.Id]));
        var padded = new byte[limit];
        Array.Fill(padded, (byte)' ');
        valid.CopyTo(padded, 0);
        using var exact = new MemoryStream(padded);
        var accepted = await Send(host, path, exact, declaredLength, chunked);
        Assert.Equal(StatusCodes.Status200OK, accepted.Response.StatusCode);
        Assert.Equal(limit, exact.Position);

        // Deliberately invalid JSON: a 413 must precede parsing, even without Content-Length.
        using var oversized = new MemoryStream(new byte[limit * 2]);
        var rejected = await Send(host, path, oversized, declaredLength, chunked);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, rejected.Response.StatusCode);
        Assert.Equal(declaredLength ? 0 : limit + 1, oversized.Position);
        using var error = await JsonDocument.ParseAsync(rejected.Response.Body, cancellationToken: Ct);
        Assert.Equal(package && !declaredLength ? "presetPackageTooLarge" : "presetTransferRequestTooLarge",
            error.RootElement.GetProperty("code").GetString());
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(root, "presets.json")));
        Assert.Equal(saved.Id, Assert.Single(store.List()).Id);
    }

    [Theory]
    [InlineData("/api/presets", PresetRequestBodyLimits.CreateBytes, false)]
    [InlineData("/api/presets", PresetRequestBodyLimits.CreateBytes, true)]
    [InlineData("/api/presets/p", PresetRequestBodyLimits.RenameBytes, false)]
    [InlineData("/api/presets/p", PresetRequestBodyLimits.RenameBytes, true)]
    [InlineData("/api/workspaces/w/documents/d/presets/capture", PresetRequestBodyLimits.CaptureBytes, false)]
    [InlineData("/api/workspaces/w/documents/d/presets/capture", PresetRequestBodyLimits.CaptureBytes, true)]
    [InlineData("/api/workspaces/w/documents/d/presets/p/preview", PresetRequestBodyLimits.PreviewBytes, false)]
    [InlineData("/api/workspaces/w/documents/d/presets/p/preview", PresetRequestBodyLimits.PreviewBytes, true)]
    [InlineData("/api/workspaces/w/documents/d/presets/p/apply", PresetRequestBodyLimits.ApplyBytes, false)]
    [InlineData("/api/workspaces/w/documents/d/presets/p/apply", PresetRequestBodyLimits.ApplyBytes, true)]
    public async Task HostRouteCapsRejectBeforeJsonOrDocumentBinding(string path, int limit, bool declaredLength)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("Toolkit:PresetRoot", TestZips.TempDir()));
        using var client = host.CreateClient();
        using var body = new MemoryStream(new byte[limit * 2]);
        var rejected = await Send(host, path, body, declaredLength, method: path == "/api/presets/p" ? "PATCH" : "POST");
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, rejected.Response.StatusCode);
        using var error = await JsonDocument.ParseAsync(rejected.Response.Body, cancellationToken: Ct);
        Assert.Equal("presetPayloadTooLarge", error.RootElement.GetProperty("code").GetString());
        Assert.Empty(host.Services.GetRequiredService<PresetStore>().List());
        // Declared sizes are rejected without touching the stream; chunked input stays finite.
        if (declaredLength) Assert.Equal(0, body.Position);
        else Assert.InRange(body.Position, limit + 1L, body.Length - 1);
    }

    private static Task<HttpContext> Send(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host,
        string path, MemoryStream body, bool declaredLength, bool chunked = true, string method = "POST") => host.Server.SendAsync(context =>
        {
            context.Request.Method = method;
            context.Request.Path = path;
            context.Request.ContentType = "application/json";
            context.Request.Body = body;
            if (declaredLength) context.Request.ContentLength = body.Length;
            else if (chunked) context.Request.Headers.TransferEncoding = "chunked";
        }, Ct);
}
