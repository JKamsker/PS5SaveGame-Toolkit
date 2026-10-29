using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SaveToolkit.Core.Tests;

public sealed class ApiTests(IsolatedApiFactory factory) : IClassFixture<IsolatedApiFactory>
{
    private readonly HttpClient _client = factory.WithWebHostBuilder(b => b.UseSetting("Toolkit:DataRoot", TestZips.TempDir())).CreateClient();

    private async Task<JsonElement> UploadAsync()
    {
        var zip = TestZips.Build(("PPSA00001/sdimg_SAVE/data.bin", [1, 2, 3, 4]));
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(zip);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "files", "dump.zip");

        var response = await _client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), form, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Upload_Open_ReadBlob_RoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var ws = await UploadAsync();
        var wsId = ws.GetProperty("id").GetString();
        var fileNode = ws.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0];
        Assert.Equal("builtin.raw", fileNode.GetProperty("matches")[0].GetProperty("pluginId").GetString());

        var open = await _client.PostAsJsonAsync($"/api/workspaces/{wsId}/files/{fileNode.GetProperty("id").GetString()}/open", new { }, ct);
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        var doc = await open.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("section", doc.GetProperty("view").GetProperty("type").GetString());

        var blob = await _client.GetByteArrayAsync(new Uri($"/api/workspaces/{wsId}/documents/{doc.GetProperty("id").GetString()}/blobs?id=content&offset=2&count=10", UriKind.Relative), ct);
        Assert.Equal([3, 4], blob);
    }

    [Fact]
    public async Task UnknownWorkspace_Returns404WithErrorEnvelope()
    {
        var response = await _client.GetAsync(new Uri("/api/workspaces/nope", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Contains("Workspace not found", body.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public async Task ReadBlob_NegativeOffset_Returns400WithErrorEnvelope(long offset)
    {
        var ct = TestContext.Current.CancellationToken;
        var workspace = await UploadAsync();
        var workspaceId = workspace.GetProperty("id").GetString();
        var fileId = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        using var opened = await _client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/files/{fileId}/open", new { }, ct);
        var document = await opened.Content.ReadFromJsonAsync<JsonElement>(ct);
        var documentId = document.GetProperty("id").GetString();

        using var response = await _client.GetAsync(new Uri(
            $"/api/workspaces/{workspaceId}/documents/{documentId}/blobs?id=content&offset={offset}&count=1", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Contains("Offset", body.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_OnReadOnlyDocument_Returns422()
    {
        var ct = TestContext.Current.CancellationToken;
        var ws = await UploadAsync();
        var wsId = ws.GetProperty("id").GetString();
        var fileId = ws.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        var doc = await (await _client.PostAsJsonAsync($"/api/workspaces/{wsId}/files/{fileId}/open", new { }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);

        var response = await _client.PostAsJsonAsync(
            $"/api/workspaces/{wsId}/documents/{doc.GetProperty("id").GetString()}/edits",
            new { edits = new[] { new { valueId = "raw.header", value = "00" } } },
            ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Export_WithoutChanges_Returns422()
    {
        var ct = TestContext.Current.CancellationToken;
        var ws = await UploadAsync();

        var response = await _client.PostAsJsonAsync($"/api/workspaces/{ws.GetProperty("id").GetString()}/export", new { }, ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
