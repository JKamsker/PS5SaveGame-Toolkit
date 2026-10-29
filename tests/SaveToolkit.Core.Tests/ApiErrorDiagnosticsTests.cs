using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class ApiErrorDiagnosticsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.WithWebHostBuilder(b =>
        b.UseEnvironment("Production").UseSetting("Toolkit:DataRoot", TestZips.TempDir())).CreateClient();

    [Theory]
    [InlineData("{")]
    [InlineData("{\"scope\":\"not-a-scope\"}")]
    public async Task Invalid_JSON_request_returns_error_envelope(string json)
    {
        string workspace = await UploadAsync();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync(new Uri($"/api/workspaces/{workspace}/export/plan", UriKind.Relative),
            content, TestContext.Current.CancellationToken);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Null_filter_value_returns_useful_400_instead_of_500()
    {
        string workspace = await UploadAsync();
        using var details = await _client.GetFromJsonAsync<JsonDocument>($"/api/workspaces/{workspace}",
            TestContext.Current.CancellationToken);
        string file = details!.RootElement.GetProperty("titles")[0].GetProperty("directories")[0]
            .GetProperty("files")[0].GetProperty("id").GetString()!;
        using var opened = await _client.PostAsJsonAsync($"/api/workspaces/{workspace}/files/{file}/open", new { },
            TestContext.Current.CancellationToken);
        var document = await opened.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        string id = document.GetProperty("id").GetString()!;

        using var response = await _client.GetAsync(new Uri(
            $"/api/workspaces/{workspace}/documents/{id}/collections?collectionId=items&filters=%7B%22category%22%3Anull%7D",
            UriKind.Relative), TestContext.Current.CancellationToken);

        var body = await AssertErrorAsync(response, HttpStatusCode.BadRequest);
        Assert.Contains("string values", body.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    private async Task<string> UploadAsync()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(TestZips.Build(("PPSA00001/sdimg_SAVE/data.bin", [1]))), "files", "dump.zip");
        using var response = await _client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), form,
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
        return body;
    }
}
