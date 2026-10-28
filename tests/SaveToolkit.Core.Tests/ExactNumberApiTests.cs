using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class ExactNumberApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Toolkit:DataRoot", TestZips.TempDir());
        builder.ConfigureServices(services => services.AddSingleton(new PluginCatalog([new ExactJsonPlugin()])));
    }).CreateClient();

    private async Task<string> OpenAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var zip = TestZips.Build(("PPSA99998/sdimg_SAVE/test.json", Encoding.UTF8.GetBytes("{\"number\":0,\"optional\":null}")));
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(zip), "files", "fixture.zip");
        var uploaded = await _client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), form, ct);
        uploaded.EnsureSuccessStatusCode();
        var workspace = await uploaded.Content.ReadFromJsonAsync<JsonElement>(ct);
        var workspaceId = workspace.GetProperty("id").GetString();
        var fileId = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        var opened = await _client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/files/{fileId}/open", new { }, ct);
        opened.EnsureSuccessStatusCode();
        var document = await opened.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(JsonValueKind.Null, document.GetProperty("view").GetProperty("children")[1].GetProperty("value").ValueKind);
        return $"/api/workspaces/{workspaceId}/documents/{document.GetProperty("id").GetString()}";
    }

    [Theory]
    [InlineData("12345678901234567890")]
    [InlineData("1.2345678901234567")]
    public async Task HttpEditAndGet_PreserveNumericText(string text)
    {
        var ct = TestContext.Current.CancellationToken;
        var path = await OpenAsync();
        var result = await _client.PostAsJsonAsync($"{path}/edits", new
        {
            edits = new object[] { new { valueId = "number", value = new { kind = "exactNumber", text } }, new { valueId = "optional", value = "" } },
        }, ct);
        result.EnsureSuccessStatusCode();
        var body = await result.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(text, body.GetProperty("changes")[0].GetProperty("newValue").GetProperty("text").GetString());
        var document = await _client.GetFromJsonAsync<JsonElement>(path, ct);
        var fields = document.GetProperty("view").GetProperty("children");
        Assert.Equal(text, fields[0].GetProperty("value").GetProperty("text").GetString());
        Assert.Equal("", fields[1].GetProperty("value").GetString());
    }

    [Fact]
    public async Task HttpMissingValue_IsRejectedWhileExplicitNullIsAccepted()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = await OpenAsync();
        var missing = await _client.PostAsJsonAsync($"{path}/edits", new { edits = new[] { new { valueId = "optional" } } }, ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        var invalid = await _client.PostAsJsonAsync($"{path}/edits", new { edits = new[] { new { valueId = "number", value = new { kind = "exactNumber", text = "01" } } } }, ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        var explicitNull = await _client.PostAsJsonAsync($"{path}/edits", new { edits = new[] { new { valueId = "optional", value = (string?)null } } }, ct);
        explicitNull.EnsureSuccessStatusCode();
        var body = await explicitNull.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("changes")[0].GetProperty("newValue").ValueKind);
    }
}
